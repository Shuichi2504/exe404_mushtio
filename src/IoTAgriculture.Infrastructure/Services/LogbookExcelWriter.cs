using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using IoTAgriculture.DTOs.Firebase;

namespace IoTAgriculture.Services;

internal static class LogbookExcelWriter
{
    private static readonly string[] Headers =
    [
        "Khung giờ",
        "Thiết bị",
        "Nhiệt độ Min (°C)",
        "Nhiệt độ Max (°C)",
        "Độ ẩm không khí Min (%)",
        "Độ ẩm không khí Max (%)",
        "Chất lượng không khí Min (ppm)",
        "Chất lượng không khí Max (ppm)",
        "Nhiệt độ tầng dưới (°C)",
        "Nhiệt độ tầng trên (°C)"
    ];

    public static byte[] Create(DailyLogbookDto logbook)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", ContentTypes);
            Write(archive, "_rels/.rels", PackageRelationships);
            Write(archive, "xl/workbook.xml", Workbook);
            Write(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships);
            Write(archive, "xl/styles.xml", Styles);
            Write(archive, "xl/worksheets/sheet1.xml", BuildWorksheet(logbook));
        }

        return output.ToArray();
    }

    private static string BuildWorksheet(DailyLogbookDto logbook)
    {
        var xml = new StringBuilder();
        xml.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><cols>""");
        for (var column = 1; column <= Headers.Length; column++)
        {
            var width = column <= 2 ? 24 : 27;
            xml.Append(CultureInfo.InvariantCulture, $"<col min=\"{column}\" max=\"{column}\" width=\"{width}\" customWidth=\"1\"/>");
        }
        xml.Append("</cols><sheetData>");
        AppendTextRow(xml, 1, Headers, header: true);

        var rowNumber = 2;
        foreach (var record in logbook.Records)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowNumber}\">");
            AppendTextCell(xml, rowNumber, 1, record.LocalTime);
            AppendTextCell(xml, rowNumber, 2, record.DeviceName);
            AppendNumberCell(xml, rowNumber, 3, record.MinTemperature ?? record.Temperature);
            AppendNumberCell(xml, rowNumber, 4, record.MaxTemperature ?? record.Temperature);
            AppendNumberCell(xml, rowNumber, 5, record.MinHumidity ?? record.Humidity);
            AppendNumberCell(xml, rowNumber, 6, record.MaxHumidity ?? record.Humidity);
            AppendNumberCell(xml, rowNumber, 7, record.MinAirQuality ?? record.AirQuality);
            AppendNumberCell(xml, rowNumber, 8, record.MaxAirQuality ?? record.AirQuality);
            AppendNumberCell(xml, rowNumber, 9, record.GroundTemperature);
            AppendNumberCell(xml, rowNumber, 10, record.TopTemperature);
            xml.Append("</row>");
            rowNumber++;
        }

        xml.Append(CultureInfo.InvariantCulture,
            $"</sheetData><autoFilter ref=\"A1:J{Math.Max(1, rowNumber - 1)}\"/><pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/></worksheet>");
        return xml.ToString();
    }

    private static void AppendTextRow(StringBuilder xml, int row, IEnumerable<string> values, bool header)
    {
        xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{row}\">");
        var column = 1;
        foreach (var value in values)
        {
            AppendTextCell(xml, row, column++, value, header ? 1 : 0);
        }
        xml.Append("</row>");
    }

    private static void AppendTextCell(StringBuilder xml, int row, int column, string? value, int style = 0)
    {
        var reference = $"{ColumnName(column)}{row}";
        var escaped = SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
        xml.Append(CultureInfo.InvariantCulture,
            $"<c r=\"{reference}\" t=\"inlineStr\" s=\"{style}\"><is><t xml:space=\"preserve\">{escaped}</t></is></c>");
    }

    private static void AppendNumberCell(StringBuilder xml, int row, int column, double? value)
    {
        if (value == null)
        {
            AppendTextCell(xml, row, column, string.Empty);
            return;
        }

        var reference = $"{ColumnName(column)}{row}";
        xml.Append(CultureInfo.InvariantCulture,
            $"<c r=\"{reference}\" s=\"2\"><v>{value.Value.ToString("0.########", CultureInfo.InvariantCulture)}</v></c>");
    }

    private static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }
        return name;
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private const string ContentTypes =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""";

    private const string PackageRelationships =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""";

    private const string Workbook =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Logbook" sheetId="1" r:id="rId1"/></sheets></workbook>""";

    private const string WorkbookRelationships =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""";

    private const string Styles =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF2E7D32"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/><xf numFmtId="2" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs></styleSheet>""";
}
