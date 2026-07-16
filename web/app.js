(function () {
  "use strict";

  var API_BASE = window.location.origin + "/api";
  var TOKEN_KEY = "mushtio.web.token";
  var API_KEY = "mushtio.web.api";
  var RELAY_KEY = "relay2";

  var state = {
    apiBase: readStorage(API_KEY) || API_BASE,
    token: readStorage(TOKEN_KEY),
    user: null,
    tab: "home",
    loading: false,
    error: "",
    assigned: [],
    sensors: {},
    histories: {},
    pump: null,
    pumpLogs: [],
    logbook: null,
    detailMetric: null,
    detailSensor: null,
    schedule: null
  };

  var metrics = [
    { id: "temperature", title: "Nhiệt độ", unit: "°C", icon: "T", color: "var(--coral)", max: 45 },
    { id: "humidity", title: "Độ ẩm không khí", unit: "%", icon: "H", color: "var(--sky)", max: 100 },
    { id: "airQuality", title: "Không khí", unit: "ppm", icon: "A", color: "var(--leaf)", max: 3000 },
    { id: "soilMoisture", title: "Độ ẩm đất", unit: "%", icon: "S", color: "var(--leaf)", max: 100 }
  ];

  var app = document.getElementById("app");

  function readStorage(key) {
    try { return localStorage.getItem(key) || ""; } catch (error) { return ""; }
  }

  function writeStorage(key, value) {
    try { localStorage.setItem(key, value); } catch (error) {}
  }

  function removeStorage(key) {
    try { localStorage.removeItem(key); } catch (error) {}
  }

  function escapeHtml(value) {
    return String(value == null ? "" : value).replace(/[&<>"']/g, function (char) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#039;" }[char];
    });
  }

  function last(items) {
    return items.length ? items[items.length - 1] : null;
  }

  function firstNumber(values) {
    for (var i = 0; i < values.length; i += 1) {
      var value = values[i];
      if (value !== null && value !== undefined && value !== "" && !Number.isNaN(Number(value))) {
        return Number(value);
      }
    }
    return null;
  }

  function average(a, b) {
    if (a === null || a === undefined || b === null || b === undefined) return null;
    return (Number(a) + Number(b)) / 2;
  }

  function formatValue(value, unit) {
    if (value === null || value === undefined || Number.isNaN(Number(value))) return "--";
    var number = Number(value);
    var text = Math.abs(number) >= 100 ? number.toFixed(0) : number.toFixed(1);
    return text + (unit ? " " + unit : "");
  }

  function api(path, options) {
    options = options || {};
    var headers = options.headers || {};
    headers["Content-Type"] = headers["Content-Type"] || "application/json";
    if (state.token) headers.Authorization = "Bearer " + state.token;
    return fetch(state.apiBase + path, {
      method: options.method || "GET",
      headers: headers,
      body: options.body
    }).then(function (response) {
      if (response.ok) {
        if (response.status === 204) return null;
        return response.json();
      }
      return response.text().then(function (text) {
        var message = "HTTP " + response.status;
        try {
          var json = JSON.parse(text);
          message = json.message || message;
        } catch (error) {
          if (text) message = text;
        }
        throw new Error(message);
      });
    });
  }

  function metricValue(sensor, kind) {
    if (!sensor) return null;
    if (kind === "temperature") {
      return firstNumber([sensor.temperature, average(sensor.groundTemperature, sensor.topTemperature), sensor.groundTemperature, sensor.topTemperature]);
    }
    if (kind === "humidity") {
      return firstNumber([sensor.humidity, sensor.topHumidity, average(sensor.groundHumidity, sensor.topHumidity), sensor.groundHumidity]);
    }
    if (kind === "airQuality") {
      return firstNumber([sensor.airQuality, sensor.air_quality]);
    }
    return firstNumber([sensor.soilMoisture, sensor.soil_moisture, sensor.groundHumidity]);
  }

  function historyValue(item, kind) {
    if (!item) return null;
    if (kind === "temperature") {
      return firstNumber([item.temperature, average(item.ground_temperature, item.top_temperature), item.groundTemperature, item.topTemperature]);
    }
    if (kind === "humidity") {
      return firstNumber([item.humidity, item.top_humidity, average(item.ground_humidity, item.top_humidity), item.groundHumidity]);
    }
    if (kind === "airQuality") {
      return firstNumber([item.air_quality, item.airQuality]);
    }
    return firstNumber([item.soil_moisture, item.soilMoisture, item.ground_humidity, item.groundHumidity]);
  }

  function parseTime(raw) {
    if (!raw) return null;
    var number = Number(raw);
    if (Number.isFinite(number)) return number < 1000000000000 ? number * 1000 : number;
    var parsed = Date.parse(String(raw).replace(" ", "T"));
    return Number.isFinite(parsed) ? parsed : null;
  }

  function isPump(device) {
    var text = ((device.deviceType || "") + " " + (device.deviceKey || "") + " " + (device.deviceName || "")).toLowerCase();
    return text.indexOf("pump") >= 0 || text.indexOf("bom") >= 0 || text.indexOf("bơm") >= 0;
  }

  function assignedSensors() {
    return state.assigned.filter(function (item) { return !isPump(item); });
  }

  function sensorKeys() {
    return Object.keys(state.sensors).sort();
  }

  function sensorName(key) {
    var assigned = state.assigned.find(function (item) { return item.deviceKey === key; });
    var sensor = state.sensors[key];
    return (assigned && assigned.deviceName) || (sensor && (sensor.deviceName || sensor.device_name)) || key;
  }

  function pumpAssignment() {
    return state.assigned.find(isPump) || null;
  }

  function statusText(kind, value) {
    if (value === null || value === undefined) return "Chưa có dữ liệu";
    if (kind === "temperature") {
      if (value > 35) return "Khẩn cấp";
      if (value > 30) return "Cảnh báo";
      if (value < 16) return "Quá thấp";
      return "Trong ngưỡng tốt";
    }
    if (kind === "airQuality") {
      if (value <= 150) return "Tốt";
      if (value <= 300) return "Trung bình";
      if (value <= 1000) return "Kém";
      return "Rất kém";
    }
    return "Đang ghi nhận";
  }

  function latestUpdate() {
    var values = sensorKeys().map(function (key) { return parseTime(state.sensors[key].timestamp); }).filter(Boolean);
    if (!values.length) return "Chưa có dữ liệu";
    var diff = Date.now() - Math.max.apply(Math, values);
    if (diff < 60000) return "vừa xong";
    if (diff < 3600000) return Math.floor(diff / 60000) + " phút trước";
    return Math.floor(diff / 3600000) + " giờ trước";
  }

  function onlineCount() {
    return sensorKeys().filter(function (key) {
      var timestamp = parseTime(state.sensors[key].timestamp);
      return timestamp && Math.abs(Date.now() - timestamp) < 120000;
    }).length;
  }

  function criticalMessage() {
    var keys = sensorKeys();
    for (var i = 0; i < keys.length; i += 1) {
      var sensor = state.sensors[keys[i]];
      var temp = metricValue(sensor, "temperature");
      var hum = metricValue(sensor, "humidity");
      var air = metricValue(sensor, "airQuality");
      var soil = metricValue(sensor, "soilMoisture");
      if (temp !== null && temp > 35) return "Nhiệt độ rất cao, cần kiểm tra nhà nấm ngay.";
      if (temp !== null && temp < 16) return "Nhiệt độ quá thấp, cần kiểm tra hệ thống.";
      if (hum !== null && (hum < 70 || hum > 96)) return "Độ ẩm không khí bất thường.";
      if (air !== null && air > 1000) return "Chất lượng không khí rất xấu.";
      if (soil !== null && soil < 30) return "Độ ẩm đất thấp.";
    }
    return "";
  }

  function navMarkup() {
    var items = [
      ["home", "Home"],
      ["sensors", "Cảm biến"],
      ["control", "Điều khiển"],
      ["logbook", "Logbook"],
      ["profile", "Tài khoản"]
    ];
    return items.map(function (item) {
      return '<button class="' + (state.tab === item[0] ? "active" : "") + '" data-tab="' + item[0] + '">' + item[1] + "</button>";
    }).join("");
  }

  function topbar(title, subtitle) {
    return '' +
      '<div class="topbar">' +
      '<div><h1>' + title + '</h1><div class="eyebrow">' + subtitle + '</div></div>' +
      '<div class="actions">' +
      '<button class="btn" data-action="refresh">Làm mới</button>' +
      '<button class="btn" data-action="logout">Đăng xuất</button>' +
      '</div></div>' +
      (state.error ? '<p class="error-text">' + escapeHtml(state.error) + '</p>' : '');
  }

  function loginView() {
    return '' +
      '<main class="login">' +
      '<section class="login-card card">' +
      '<div class="login-art">' +
      '<div class="brand"><div class="brand-mark">M</div><span>Mushtio</span></div>' +
      '<div><h1>Dashboard trang trại nấm IoT</h1><p style="margin-top:14px;color:rgba(255,255,255,.84);font-weight:850">Web quản lý tách riêng mobile, dùng API backend và chỉ hiển thị thiết bị được admin gán.</p></div>' +
      '<div class="hero-stats"><div class="hero-chip"><span>Cảm biến</span><b>Chi tiết như app</b></div><div class="hero-chip"><span>Logbook</span><b>Min / Max theo giờ</b></div></div>' +
      '</div>' +
      '<form class="login-form form" id="loginForm">' +
      '<div><h2>Đăng nhập</h2><p class="muted" style="margin-top:6px">Quản lý trang trại IoT của bạn</p></div>' +
      '<div class="field"><label>Email hoặc số điện thoại</label><input name="identifier" autocomplete="username" required></div>' +
      '<div class="field"><label>Mật khẩu</label><input name="password" type="password" autocomplete="current-password" required></div>' +
      '<div class="field"><label>API base URL</label><input name="apiBase" value="' + escapeHtml(state.apiBase) + '" required></div>' +
      '<button class="btn primary" type="submit">' + (state.loading ? "Đang đăng nhập..." : "Đăng nhập") + '</button>' +
      (state.error ? '<p class="error-text">' + escapeHtml(state.error) + '</p>' : '') +
      '</form>' +
      '</section></main>';
  }

  function shell(content) {
    return '' +
      '<div class="app-shell">' +
      '<aside class="sidebar"><div class="brand"><div class="brand-mark">M</div><span>Mushtio</span></div><nav class="nav">' + navMarkup() + '</nav><div class="sidebar-footer">API: ' + escapeHtml(state.apiBase) + '</div></aside>' +
      '<main class="content">' + content + '</main>' +
      '<nav class="mobile-nav">' + navMarkup() + '</nav>' +
      '</div>' +
      detailDrawer();
  }

  function homeView() {
    var nameParts = ((state.user && state.user.fullName) || "nhà vườn").trim().split(/\s+/);
    var name = last(nameParts) || "nhà vườn";
    var alert = criticalMessage();
    return topbar("Chào, " + escapeHtml(name), "Dashboard trang trại nấm IoT") +
      '<section class="hero card ' + (alert ? "alert" : "") + '">' +
      '<div class="hero-head"><div class="bubble">IoT</div><span class="pill"><span class="dot"></span>' + (alert ? "Cần xử lý" : onlineCount() + " online") + '</span></div>' +
      '<div><h2>' + (alert ? "Có cảnh báo" : "Trang trại ổn định") + '</h2><p>' + escapeHtml(alert || (sensorKeys().length + " cảm biến đang được theo dõi")) + '</p>' +
      '<div class="hero-stats"><div class="hero-chip"><span>Cảm biến</span><b>' + sensorKeys().length + ' thiết bị</b></div><div class="hero-chip"><span>Cập nhật</span><b>' + latestUpdate() + '</b></div></div></div></section>' +
      '<section class="section"><div class="section-head"><h2>Dữ liệu cảm biến</h2><span class="muted">Bấm để xem chi tiết</span></div><div class="grid four">' + metrics.map(metricCard).join("") + '</div></section>' +
      '<section class="section grid two">' + pumpCard() + logbookSummaryCard() + '</section>' +
      '<section class="section"><div class="section-head"><h2>Thiết bị được gán</h2><span class="muted">' + state.assigned.length + ' thiết bị</span></div>' + devicesGrid(false) + '</section>';
  }

  function metricCard(metric) {
    var values = sensorKeys().map(function (key) { return metricValue(state.sensors[key], metric.id); }).filter(function (value) { return value !== null; });
    var value = values.length ? values[values.length - 1] : null;
    return '' +
      '<button class="metric-card card" data-metric="' + metric.id + '">' +
      '<span class="bubble" style="background:' + metric.color + '">' + metric.icon + '</span>' +
      '<span><span class="metric-value" style="color:' + metric.color + '">' + formatValue(value, metric.unit) + '</span><h3>' + metric.title + '</h3><small>' + statusText(metric.id, value) + '</small></span>' +
      '</button>';
  }

  function pumpCard() {
    var assignment = pumpAssignment();
    var on = Boolean(state.pump && state.pump.relay2);
    return '' +
      '<article class="wide-card card pad"><span class="bubble" style="background:' + (on ? "var(--leaf)" : "var(--muted)") + '">P</span>' +
      '<div class="grow"><h3>Máy bơm</h3><span class="metric-value" style="font-size:20px;color:' + (on ? "var(--leaf)" : "var(--muted)") + '">' + (assignment ? (on ? "Đang chạy" : "Đang nghỉ") : "Chưa gán") + '</span>' +
      '<p class="muted">' + escapeHtml(assignment ? (assignment.deviceName || assignment.deviceKey) : "Admin chưa gán máy bơm") + '</p></div>' +
      '<button class="switch ' + (on ? "on" : "") + '" data-action="toggle-pump" ' + (assignment ? "" : "disabled") + '><span></span></button></article>';
  }

  function logbookSummaryCard() {
    var count = state.logbook && state.logbook.records ? state.logbook.records.length : 0;
    return '' +
      '<article class="wide-card card pad"><span class="bubble" style="background:var(--violet)">L</span><div class="grow"><h3>Logbook điện tử</h3><span class="metric-value" style="font-size:20px;color:var(--violet)">' + count + ' dòng hôm nay</span><p class="muted">Min / max theo từng khung giờ</p></div><button class="btn icon" data-tab="logbook">›</button></article>';
  }

  function sensorsView() {
    return topbar("Cảm biến", "Chi tiết từng loại dữ liệu giống luồng mobile") +
      '<section class="hero card"><div class="hero-head"><div class="bubble">S</div><span class="pill">' + sensorKeys().length + ' cảm biến</span></div><div><h2>Dữ liệu môi trường</h2><p>Bấm từng chỉ số để mở panel chi tiết, biểu đồ và thống kê thấp/cao.</p></div></section>' +
      '<section class="section"><div class="grid four">' + metrics.map(metricCard).join("") + '</div></section>' +
      '<section class="section"><div class="section-head"><h2>Danh sách cảm biến</h2><span class="muted">Theo thiết bị được gán</span></div>' + sensorList() + '</section>';
  }

  function sensorList() {
    var keys = sensorKeys();
    if (!keys.length) return '<div class="card empty">Chưa có cảm biến được gán cho tài khoản.</div>';
    return '<div class="grid three">' + keys.map(function (key) {
      var sensor = state.sensors[key];
      return '<article class="device-tile card pad"><span class="bubble">S</span><div><h3>' + escapeHtml(sensorName(key)) + '</h3><p class="muted">' + escapeHtml(key) + '</p><p class="muted">Nhiệt ' + formatValue(metricValue(sensor, "temperature"), "°C") + ' · Ẩm đất ' + formatValue(metricValue(sensor, "soilMoisture"), "%") + '</p></div></article>';
    }).join("") + '</div>';
  }

  function devicesGrid(expanded) {
    var items = state.assigned.length ? state.assigned : sensorKeys().map(function (key) {
      return { deviceKey: key, deviceName: sensorName(key), deviceType: "sensor" };
    });
    if (!items.length) return '<div class="card empty">Chưa có thiết bị được gán. Vui lòng nhờ admin gán thiết bị.</div>';
    var visible = expanded ? items : items.slice(0, 4);
    return '<div class="grid four">' + visible.map(function (item) {
      return '<article class="device-tile card pad"><span class="bubble">' + (isPump(item) ? "P" : "S") + '</span><div><h3>' + escapeHtml(item.deviceName || item.deviceKey) + '</h3><p class="muted">' + escapeHtml(item.deviceType || "device") + ' · ' + escapeHtml(item.deviceKey) + '</p></div></article>';
    }).join("") + '</div>';
  }

  function controlView() {
    var assignment = pumpAssignment();
    var on = Boolean(state.pump && state.pump.relay2);
    if (!assignment) {
      return topbar("Điều khiển máy bơm", "Tài khoản này chưa được gán máy bơm") + '<div class="card empty">Admin chưa gán máy bơm cho tài khoản.</div>';
    }
    return topbar("Điều khiển máy bơm", on ? "Hệ thống đang tưới" : "Hệ thống đang chờ") +
      '<section class="hero card" style="background:linear-gradient(135deg,' + (on ? "var(--deep),var(--leaf)" : "#1b2530,#51606d") + ')"><div class="hero-head"><div class="bubble">P</div><span class="pill">' + (on ? "Đang chạy" : "Đang nghỉ") + '</span></div><div><h2>' + (on ? "ON" : "OFF") + '</h2><p>' + escapeHtml(assignment.deviceName || assignment.deviceKey) + '</p><div class="hero-stats"><div class="hero-chip"><span>Lần thao tác cuối</span><b>' + escapeHtml((state.pump && state.pump.lastActionLocal) || "Chưa có") + '</b></div><div class="hero-chip"><span>Chế độ</span><b>' + escapeHtml((state.pump && state.pump.lastActionSource) || "manual") + '</b></div></div></div></section>' +
      '<section class="section grid two"><article class="card pad"><div class="section-head"><h2>Bật/tắt bơm</h2><button class="switch ' + (on ? "on" : "") + '" data-action="toggle-pump"><span></span></button></div><p class="muted">Điều khiển relay chính giống màn Control trên mobile.</p></article>' + scheduleCard() + '</section>' +
      '<section class="section"><div class="section-head"><h2>Lịch sử hoạt động</h2><span class="muted">' + state.pumpLogs.length + ' dòng</span></div>' + pumpLogsTable() + '</section>';
  }

  function scheduleCard() {
    var sensorOptions = sensorKeys().map(function (key) { return '<option value="' + escapeHtml(key) + '">' + escapeHtml(sensorName(key)) + '</option>'; }).join("");
    return '' +
      '<article class="card pad"><h2>Lịch tưới thông minh</h2><form class="form" id="scheduleForm" style="margin-top:14px">' +
      '<div class="grid two"><div class="field"><label>Chu kỳ (phút)</label><input name="intervalMinutes" type="number" value="' + escapeHtml((state.schedule && state.schedule.intervalMinutes) || 180) + '"></div><div class="field"><label>Thời lượng (phút)</label><input name="durationMinutes" type="number" value="' + escapeHtml((state.schedule && state.schedule.durationMinutes) || 10) + '"></div></div>' +
      '<div class="field"><label>Giờ bắt đầu</label><input name="startTime" value="' + escapeHtml((state.schedule && state.schedule.startTime) || "06:00") + '"></div>' +
      '<div class="grid two"><div class="field"><label>Ngưỡng đất (%)</label><input name="soilMoistureThreshold" type="number" value="' + escapeHtml((state.schedule && state.schedule.soilMoistureThreshold) || 30) + '"></div><div class="field"><label>Cảm biến đất</label><select name="sensorKey">' + sensorOptions + '</select></div></div>' +
      '<button class="btn primary" type="submit">Lưu lịch tưới</button></form></article>';
  }

  function pumpLogsTable() {
    if (!state.pumpLogs.length) return '<div class="card empty">Chưa có lịch sử máy bơm.</div>';
    return '<div class="table-wrap"><table><thead><tr><th>Thời gian</th><th>Relay</th><th>Trạng thái</th><th>Nguồn</th><th>Người thao tác</th></tr></thead><tbody>' + state.pumpLogs.map(function (log) {
      return '<tr><td>' + escapeHtml(log.localTime || log.timestamp || "") + '</td><td>' + escapeHtml(log.relayKey || "") + '</td><td>' + (log.value ? "Bật" : "Tắt") + '</td><td>' + escapeHtml(log.source || "") + '</td><td>' + escapeHtml(log.actorName || "") + '</td></tr>';
    }).join("") + '</tbody></table></div>';
  }

  function logbookView() {
    return topbar("Logbook điện tử", "Giá trị thấp nhất và cao nhất theo giờ") +
      '<section class="section"><div class="actions" style="justify-content:flex-start"><button class="btn primary" data-action="generate-logbook">Tạo lại hôm nay</button><a class="btn" href="' + escapeHtml(state.apiBase) + '/logbooks/today/csv" target="_blank" rel="noreferrer">Tải CSV</a></div></section>' +
      '<section class="section">' + logbookTable() + '</section>';
  }

  function logbookTable() {
    var rows = state.logbook && state.logbook.records ? state.logbook.records : [];
    if (!rows.length) return '<div class="card empty">Chưa có dữ liệu logbook hôm nay.</div>';
    return '<div class="table-wrap"><table><thead><tr><th>Giờ</th><th>Thiết bị</th><th>Nhiệt min/max</th><th>Ẩm KK min/max</th><th>Không khí min/max</th><th>Đất min/max</th></tr></thead><tbody>' + rows.map(function (row) {
      return '<tr><td>' + escapeHtml(row.periodStartLocal || row.localTime || "") + '</td><td>' + escapeHtml(row.deviceName || row.deviceKey || "") + '</td><td>' + formatValue(row.minTemperature, "°C") + ' / ' + formatValue(row.maxTemperature, "°C") + '</td><td>' + formatValue(row.minHumidity, "%") + ' / ' + formatValue(row.maxHumidity, "%") + '</td><td>' + formatValue(row.minAirQuality, "ppm") + ' / ' + formatValue(row.maxAirQuality, "ppm") + '</td><td>' + formatValue(row.minSoilMoisture, "%") + ' / ' + formatValue(row.maxSoilMoisture, "%") + '</td></tr>';
    }).join("") + '</tbody></table></div>';
  }

  function profileView() {
    var user = state.user || {};
    return topbar("Tài khoản", "Thông tin phiên đăng nhập") +
      '<section class="grid two"><article class="card pad"><span class="muted">Họ tên</span><h2>' + escapeHtml(user.fullName || "--") + '</h2></article><article class="card pad"><span class="muted">Vai trò</span><h2>' + escapeHtml(user.role || "--") + '</h2></article><article class="card pad"><span class="muted">Email</span><h2>' + escapeHtml(user.email || "--") + '</h2></article><article class="card pad"><span class="muted">Số điện thoại</span><h2>' + escapeHtml(user.phoneNumber || "--") + '</h2></article></section>' +
      '<section class="section"><h2>Thiết bị của tôi</h2><div style="margin-top:12px">' + devicesGrid(true) + '</div></section>';
  }

  function detailDrawer() {
    if (!state.detailMetric) return '<div class="drawer" id="drawer"></div>';
    var metric = metrics.find(function (item) { return item.id === state.detailMetric; }) || metrics[0];
    var keys = sensorKeys();
    var selected = state.detailSensor || keys[0] || "";
    var sensor = state.sensors[selected];
    var history = state.histories[selected] || [];
    var current = metricValue(sensor, metric.id);
    var values = history.map(function (item) { return historyValue(item, metric.id); }).filter(function (value) { return value !== null; });
    var min = values.length ? Math.min.apply(Math, values) : null;
    var max = values.length ? Math.max.apply(Math, values) : null;
    return '<div class="drawer open" id="drawer"><aside class="panel">' +
      '<div class="topbar"><div><h1>' + metric.title + '</h1><div class="eyebrow">' + escapeHtml(sensorName(selected)) + '</div></div><button class="btn icon" data-action="close-detail">×</button></div>' +
      '<section class="hero card" style="background:linear-gradient(135deg,' + metric.color + ',#143b30)"><div class="hero-head"><div class="bubble">' + metric.icon + '</div><span class="pill">' + statusText(metric.id, current) + '</span></div><div><h2>' + formatValue(current, metric.unit) + '</h2><p>' + escapeHtml(selected) + '</p></div></section>' +
      '<section class="section"><div class="section-head"><h2>Cảm biến</h2><span class="muted">' + keys.length + ' thiết bị</span></div><div class="actions" style="justify-content:flex-start">' + keys.map(function (key) { return '<button class="btn ' + (key === selected ? "primary" : "") + '" data-detail-sensor="' + escapeHtml(key) + '">' + escapeHtml(sensorName(key)) + '</button>'; }).join("") + '</div></section>' +
      '<section class="section stat-strip"><div class="stat"><span class="muted">Thấp nhất</span><b>' + formatValue(min, metric.unit) + '</b></div><div class="stat"><span class="muted">Cao nhất</span><b>' + formatValue(max, metric.unit) + '</b></div></section>' +
      '<section class="section card pad"><div class="section-head"><h2>Biểu đồ 24 giờ</h2><span class="muted">' + history.length + ' mẫu</span></div>' + chart(history, metric) + '</section>' +
      '</aside></div>';
  }

  function chart(history, metric) {
    var points = history.map(function (item) {
      return { ts: parseTime(item.timestamp), value: historyValue(item, metric.id) };
    }).filter(function (item) {
      return item.ts && item.value !== null;
    }).sort(function (a, b) {
      return a.ts - b.ts;
    }).slice(-120);
    if (!points.length) return '<div class="chart empty">Đang thu thập dữ liệu...</div>';
    var w = 720;
    var h = 260;
    var p = 28;
    var minY = metric.id === "temperature" ? 10 : 0;
    var maxY = Math.max.apply(Math, [metric.max].concat(points.map(function (item) { return Number(item.value); })));
    var minT = points[0].ts;
    var maxT = points[points.length - 1].ts || minT + 1;
    function xy(point) {
      var x = p + ((point.ts - minT) / Math.max(1, maxT - minT)) * (w - p * 2);
      var y = h - p - ((Number(point.value) - minY) / Math.max(1, maxY - minY)) * (h - p * 2);
      return [x.toFixed(1), y.toFixed(1)];
    }
    var poly = points.map(function (point) { return xy(point).join(","); }).join(" ");
    return '<svg class="chart" viewBox="0 0 ' + w + ' ' + h + '"><polyline points="' + poly + '" fill="none" stroke="' + metric.color + '" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"></polyline></svg>';
  }

  function render() {
    if (!state.token) {
      app.innerHTML = loginView();
      bind();
      return;
    }
    var body = state.loading ? topbar("Đang tải", "Đang lấy dữ liệu từ backend") + '<div class="card empty">Vui lòng chờ...</div>' :
      state.tab === "sensors" ? sensorsView() :
      state.tab === "control" ? controlView() :
      state.tab === "logbook" ? logbookView() :
      state.tab === "profile" ? profileView() : homeView();
    app.innerHTML = shell(body);
    bind();
  }

  function bind() {
    var loginForm = document.getElementById("loginForm");
    if (loginForm) {
      loginForm.addEventListener("submit", function (event) {
        event.preventDefault();
        var form = new FormData(loginForm);
        state.apiBase = String(form.get("apiBase") || API_BASE).replace(/\/$/, "");
        writeStorage(API_KEY, state.apiBase);
        state.loading = true;
        state.error = "";
        render();
        api("/auth/login", {
          method: "POST",
          body: JSON.stringify({ identifier: form.get("identifier"), password: form.get("password") })
        }).then(function (session) {
          state.token = session.token || session.Token || "";
          writeStorage(TOKEN_KEY, state.token);
          return loadAll();
        }).catch(function (error) {
          state.error = error.message || "Đăng nhập thất bại";
          state.loading = false;
          render();
        });
      });
    }

    document.querySelectorAll("[data-tab]").forEach(function (button) {
      button.addEventListener("click", function () {
        state.tab = button.getAttribute("data-tab");
        state.detailMetric = null;
        render();
      });
    });
    document.querySelectorAll("[data-metric]").forEach(function (button) {
      button.addEventListener("click", function () {
        state.detailMetric = button.getAttribute("data-metric");
        state.detailSensor = sensorKeys()[0] || null;
        render();
      });
    });
    document.querySelectorAll("[data-detail-sensor]").forEach(function (button) {
      button.addEventListener("click", function () {
        state.detailSensor = button.getAttribute("data-detail-sensor");
        render();
      });
    });
    var drawer = document.getElementById("drawer");
    if (drawer) {
      drawer.addEventListener("click", function (event) {
        if (event.target === drawer) {
          state.detailMetric = null;
          render();
        }
      });
    }
    document.querySelectorAll("[data-action]").forEach(function (button) {
      button.addEventListener("click", function () {
        var action = button.getAttribute("data-action");
        if (action === "logout") logout();
        if (action === "refresh") loadAll();
        if (action === "toggle-pump") togglePump();
        if (action === "close-detail") { state.detailMetric = null; render(); }
        if (action === "generate-logbook") generateLogbook();
      });
    });
    var scheduleForm = document.getElementById("scheduleForm");
    if (scheduleForm) {
      scheduleForm.addEventListener("submit", function (event) {
        event.preventDefault();
        saveSchedule(new FormData(scheduleForm));
      });
    }
  }

  function logout() {
    removeStorage(TOKEN_KEY);
    state.token = "";
    state.user = null;
    render();
  }

  function loadAll() {
    state.loading = true;
    state.error = "";
    render();
    return api("/auth/me")
      .then(function (user) {
        state.user = user;
        return api("/me/devices");
      })
      .then(function (devices) {
        state.assigned = Array.isArray(devices) ? devices : [];
        return loadSensors();
      })
      .then(loadPump)
      .then(loadLogbook)
      .catch(function (error) {
        state.error = error.message || "Không tải được dữ liệu";
        if (String(state.error).indexOf("401") >= 0) logout();
      })
      .then(function () {
        state.loading = false;
        render();
      });
  }

  function loadSensors() {
    var assigned = assignedSensors();
    var keyPromise = assigned.length ? Promise.resolve(assigned.map(function (item) { return item.deviceKey; })) : api("/sensors");
    return keyPromise.then(function (keys) {
      keys = Array.isArray(keys) ? keys : [];
      return Promise.all(keys.map(function (key) {
        return api("/sensors/" + encodeURIComponent(key)).then(function (sensor) {
          state.sensors[key] = sensor;
          return loadHistory(key);
        }).catch(function () {});
      }));
    });
  }

  function loadHistory(key) {
    var to = Date.now();
    var from = to - 24 * 60 * 60 * 1000;
    return api("/sensors/" + encodeURIComponent(key) + "/history?from=" + from + "&to=" + to).then(function (rows) {
      state.histories[key] = Array.isArray(rows) ? rows : [];
    }).catch(function () {
      state.histories[key] = [];
    });
  }

  function loadPump() {
    var pump = pumpAssignment();
    if (!pump) {
      state.pump = null;
      state.pumpLogs = [];
      state.schedule = null;
      return Promise.resolve();
    }
    return api("/devices/" + encodeURIComponent(pump.deviceKey)).then(function (pumpState) {
      state.pump = pumpState;
    }).catch(function () {
      state.pump = null;
    }).then(function () {
      return api("/devices/" + encodeURIComponent(pump.deviceKey) + "/logs?limit=100").then(function (logs) {
        state.pumpLogs = Array.isArray(logs) ? logs : [];
      }).catch(function () {
        state.pumpLogs = [];
      });
    }).then(function () {
      return api("/devices/" + encodeURIComponent(pump.deviceKey) + "/schedule/" + RELAY_KEY).then(function (schedule) {
        state.schedule = schedule;
      }).catch(function () {
        state.schedule = null;
      });
    });
  }

  function loadLogbook() {
    return api("/logbooks/today").then(function (logbook) {
      state.logbook = logbook;
    }).catch(function () {
      state.logbook = null;
    });
  }

  function togglePump() {
    var pump = pumpAssignment();
    if (!pump) return;
    var current = Boolean(state.pump && state.pump.relay2);
    api("/devices/" + encodeURIComponent(pump.deviceKey) + "/relay/" + RELAY_KEY, {
      method: "PUT",
      body: JSON.stringify({ value: !current })
    }).then(loadPump).then(render).catch(function (error) {
      state.error = error.message || "Không bật/tắt được bơm";
      render();
    });
  }

  function saveSchedule(form) {
    var pump = pumpAssignment();
    if (!pump) return;
    var payload = {
      enabled: true,
      intervalMinutes: Number(form.get("intervalMinutes") || 180),
      durationMinutes: Number(form.get("durationMinutes") || 10),
      startTime: String(form.get("startTime") || "06:00"),
      smartEnabled: true,
      sensorKey: String(form.get("sensorKey") || ""),
      soilMoistureThreshold: Number(form.get("soilMoistureThreshold") || 30),
      maxDurationMinutes: 10,
      cooldownMinutes: 30
    };
    api("/devices/" + encodeURIComponent(pump.deviceKey) + "/schedule/" + RELAY_KEY, {
      method: "PUT",
      body: JSON.stringify(payload)
    }).then(function (schedule) {
      state.schedule = schedule;
      render();
    }).catch(function (error) {
      state.error = error.message || "Không lưu được lịch tưới";
      render();
    });
  }

  function generateLogbook() {
    api("/logbooks/today/generate", { method: "POST", body: "{}" }).then(function (logbook) {
      state.logbook = logbook;
      render();
    }).catch(function (error) {
      state.error = error.message || "Không tạo được logbook";
      render();
    });
  }

  render();
  if (state.token) loadAll();
})();
