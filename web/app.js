const DEFAULT_API = `${window.location.origin}/api`;
const TOKEN_KEY = "mushtio.web.token";
const API_KEY = "mushtio.web.api";
const app = document.querySelector("#app");

function storageGet(key) {
  try {
    return localStorage.getItem(key) || "";
  } catch {
    return "";
  }
}

function storageSet(key, value) {
  try {
    localStorage.setItem(key, value);
  } catch {}
}

function storageRemove(key) {
  try {
    localStorage.removeItem(key);
  } catch {}
}

function lastItem(items) {
  return items.length ? items[items.length - 1] : undefined;
}

const state = {
  apiBase: storageGet(API_KEY) || DEFAULT_API,
  token: storageGet(TOKEN_KEY),
  user: null,
  view: "home",
  assigned: [],
  sensors: new Map(),
  histories: new Map(),
  pump: null,
  pumpLogs: [],
  logbook: null,
  loading: false,
  selectedMetric: null,
  selectedSensorKey: null,
  error: ""
};

const metrics = [
  { id: "temperature", label: "Nhiệt độ", unit: "°C", icon: "🌡", color: "var(--coral)", max: 45 },
  { id: "humidity", label: "Độ ẩm không khí", unit: "%", icon: "💧", color: "var(--sky)", max: 100 },
  { id: "airQuality", label: "Không khí", unit: "ppm", icon: "〰", color: "var(--leaf)", max: 3000 },
  { id: "soilMoisture", label: "Độ ẩm đất", unit: "%", icon: "⌁", color: "var(--leaf)", max: 100 }
];

function api(path, options = {}) {
  const headers = { ...(options.headers || {}) };
  if (!(options.body instanceof FormData)) headers["Content-Type"] = headers["Content-Type"] || "application/json";
  if (state.token) headers.Authorization = `Bearer ${state.token}`;
  return fetch(`${state.apiBase}${path}`, { ...options, headers }).then(async (res) => {
    if (res.ok) return res.status === 204 ? null : res.json();
    let message = `HTTP ${res.status}`;
    try {
      const body = await res.json();
      message = body.message || message;
    } catch {}
    throw new Error(message);
  });
}

function fmt(value, unit = "") {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return "--";
  const number = Number(value);
  const text = Math.abs(number) >= 100 ? number.toFixed(0) : number.toFixed(1);
  return `${text}${unit ? ` ${unit}` : ""}`;
}

function valueFor(sensor, kind) {
  if (!sensor) return null;
  if (kind === "temperature") return first(sensor.temperature, avg(sensor.groundTemperature, sensor.topTemperature), sensor.groundTemperature, sensor.topTemperature);
  if (kind === "humidity") return first(sensor.humidity, sensor.topHumidity, avg(sensor.groundHumidity, sensor.topHumidity), sensor.groundHumidity);
  if (kind === "airQuality") return first(sensor.airQuality, sensor.air_quality);
  return first(sensor.soilMoisture, sensor.soil_moisture, sensor.groundHumidity);
}

function historyValue(item, kind) {
  if (kind === "temperature") return first(item.temperature, avg(item.ground_temperature, item.top_temperature), item.groundTemperature, item.topTemperature);
  if (kind === "humidity") return first(item.humidity, item.top_humidity, avg(item.ground_humidity, item.top_humidity), item.groundHumidity);
  if (kind === "airQuality") return first(item.air_quality, item.airQuality);
  return first(item.soil_moisture, item.soilMoisture, item.ground_humidity, item.groundHumidity);
}

function first(...values) {
  return values.find((x) => x !== null && x !== undefined && x !== "" && !Number.isNaN(Number(x))) ?? null;
}

function avg(a, b) {
  if (a === null || a === undefined || b === null || b === undefined) return null;
  return (Number(a) + Number(b)) / 2;
}

function isPump(item) {
  const text = `${item.deviceType || ""} ${item.deviceKey || ""} ${item.deviceName || ""}`.toLowerCase();
  return text.includes("pump") || text.includes("bom");
}

function isSensorAssignment(item) {
  return !isPump(item);
}

function sensorName(key) {
  const assigned = state.assigned.find((x) => x.deviceKey === key);
  const sensor = state.sensors.get(key);
  return assigned?.deviceName || sensor?.deviceName || sensor?.device_name || key;
}

async function loadInitial() {
  state.loading = true;
  state.error = "";
  render();
  try {
    state.user = await api("/auth/me");
    state.assigned = await api("/me/devices");
    await loadSensors();
    await loadPump();
    await loadLogbook();
  } catch (err) {
    state.error = err.message;
    if (String(err.message).includes("401")) logout(false);
  } finally {
    state.loading = false;
    render();
  }
}

async function loadSensors() {
  const assignedSensors = state.assigned.filter(isSensorAssignment);
  const keys = assignedSensors.length ? assignedSensors.map((x) => x.deviceKey) : await api("/sensors");
  const entries = await Promise.all(keys.map(async (key) => {
    try {
      const sensor = await api(`/sensors/${encodeURIComponent(key)}`);
      return [key, sensor];
    } catch {
      return null;
    }
  }));
  state.sensors = new Map(entries.filter(Boolean));
  await Promise.all([...state.sensors.keys()].map(loadHistory));
}

async function loadHistory(key) {
  const to = Date.now();
  const from = to - 24 * 60 * 60 * 1000;
  try {
    const rows = await api(`/sensors/${encodeURIComponent(key)}/history?from=${from}&to=${to}`);
    state.histories.set(key, rows);
  } catch {
    state.histories.set(key, []);
  }
}

async function loadPump() {
  const pump = state.assigned.find(isPump);
  if (!pump) {
    state.pump = null;
    state.pumpLogs = [];
    return;
  }
  try {
    state.pump = { assignment: pump, state: await api(`/devices/${encodeURIComponent(pump.deviceKey)}`) };
    state.pumpLogs = await api(`/devices/${encodeURIComponent(pump.deviceKey)}/logs?limit=20`);
  } catch {
    state.pump = { assignment: pump, state: null };
    state.pumpLogs = [];
  }
}

async function loadLogbook() {
  try {
    state.logbook = await api("/logbooks/today");
  } catch {
    state.logbook = null;
  }
}

async function refresh() {
  await loadInitial();
}

async function togglePump() {
  if (!state.pump) return;
  const pumpKey = state.pump.assignment.deviceKey;
  const current = Boolean(state.pump.state?.relay2);
  await api(`/devices/${encodeURIComponent(pumpKey)}/relay/relay2`, {
    method: "PUT",
    body: JSON.stringify({ value: !current })
  });
  await loadPump();
  render();
}

function loginTemplate() {
  return `
    <main class="login-shell">
      <section class="login-card card">
        <div class="login-art">
          <div class="brand"><span class="brand-mark">M</span><span>Mushtio</span></div>
          <div>
            <h1>Dashboard trang trại nấm IoT</h1>
            <p style="margin-top:14px;color:rgba(255,255,255,.84);font-weight:800">Web riêng, dùng API backend, chỉ hiển thị thiết bị được admin gán cho tài khoản.</p>
          </div>
          <div class="hero-stats">
            <div class="hero-chip"><span>Cảm biến</span><b>Chi tiết từng loại</b></div>
            <div class="hero-chip"><span>Logbook</span><b>Min / Max theo giờ</b></div>
          </div>
        </div>
        <form class="login-form" id="loginForm">
          <h2>Đăng nhập</h2>
          <p class="muted" style="margin-top:6px">Dùng tài khoản backend hiện tại.</p>
          <div class="field"><label>Email hoặc số điện thoại</label><input name="identifier" autocomplete="username" required /></div>
          <div class="field"><label>Mật khẩu</label><input name="password" type="password" autocomplete="current-password" required /></div>
          <div class="field"><label>API base URL</label><input name="apiBase" value="${escapeHtml(state.apiBase)}" required /></div>
          <button class="btn primary" style="width:100%;margin-top:18px" type="submit">Đăng nhập</button>
          ${state.error ? `<div class="error">${escapeHtml(state.error)}</div>` : ""}
        </form>
      </section>
    </main>
  `;
}

function shellTemplate(content) {
  const nav = [
    ["home", "⌂", "Home"],
    ["devices", "▣", "Thiết bị"],
    ["logbook", "☷", "Logbook"],
    ["profile", "○", "Tài khoản"]
  ];
  const navButtons = nav.map(([id, icon, label]) => `<button data-view="${id}" class="${state.view === id ? "active" : ""}"><span>${icon}</span><span>${label}</span></button>`).join("");
  return `
    <div class="app">
      <aside class="sidebar">
        <div class="brand"><span class="brand-mark">M</span><span>Mushtio</span></div>
        <nav class="nav">${navButtons}</nav>
      </aside>
      <main class="content">${content}</main>
      <nav class="bottom-nav">${navButtons}</nav>
    </div>
    <div class="drawer ${state.selectedMetric ? "open" : ""}" id="drawer">${state.selectedMetric ? detailTemplate() : ""}</div>
  `;
}

function homeTemplate() {
  const alert = criticalMessage();
  const onlineCount = [...state.sensors.values()].filter(isOnline).length;
  return `
    ${topbar("Chào ${escapeHtml(lastItem((state.user?.fullName || "nhà vườn").split(" ")))}", "Dashboard trang trại nấm IoT")}
    <section class="hero card ${alert ? "alert" : ""}">
      <div class="hero-row">
        <span class="icon-bubble">♧</span>
        <span class="pill"><span class="dot"></span>${alert ? "Cần xử lý" : `${onlineCount} online`}</span>
      </div>
      <div>
        <h2>${alert ? "Có cảnh báo" : "Trang trại ổn định"}</h2>
        <p>${escapeHtml(alert || `${state.sensors.size} cảm biến đang được theo dõi`)}</p>
        <div class="hero-stats">
          <div class="hero-chip"><span>Cảm biến</span><b>${state.sensors.size} thiết bị</b></div>
          <div class="hero-chip"><span>Cập nhật</span><b>${latestUpdateLabel()}</b></div>
        </div>
      </div>
    </section>
    <section class="section">
      <div class="section-head"><h2>Dữ liệu cảm biến</h2><span class="muted">Bấm để xem chi tiết</span></div>
      <div class="grid four">${metrics.map(metricCard).join("")}</div>
    </section>
    <section class="section grid two">
      ${pumpCard()}
      ${logbookCard()}
    </section>
    <section class="section">
      <div class="section-head"><h2>Thiết bị được gán</h2><span class="muted">${state.assigned.length} thiết bị</span></div>
      ${devicesGrid()}
    </section>
  `;
}

function topbar(title, subtitle) {
  return `
    <div class="topbar">
      <div><h1>${title}</h1><div class="eyebrow" style="margin-top:7px">${subtitle}</div></div>
      <div class="actions">
        <button class="btn" id="refreshBtn">Làm mới</button>
        <button class="btn" id="logoutBtn">Đăng xuất</button>
      </div>
    </div>
    ${state.error ? `<div class="error">${escapeHtml(state.error)}</div>` : ""}
  `;
}

function metricCard(metric) {
  const values = [...state.sensors.values()].map((x) => valueFor(x, metric.id)).filter((x) => x !== null);
  const latest = values.length ? lastItem(values) : null;
  return `
    <button class="metric-card card" data-metric="${metric.id}">
      <span class="icon-bubble" style="background:${metric.color}">${metric.icon}</span>
      <span>
        <span class="metric-value" style="color:${metric.color}">${fmt(latest, metric.unit)}</span>
        <h3>${metric.label}</h3>
        <small>${statusFor(metric.id, latest)}</small>
      </span>
    </button>
  `;
}

function pumpCard() {
  const pump = state.pump;
  const on = Boolean(pump?.state?.relay2);
  return `
    <article class="wide-card card pad">
      <span class="icon-bubble" style="background:${on ? "var(--leaf)" : "var(--muted)"}">💧</span>
      <div class="grow">
        <h3>Máy bơm</h3>
        <p class="metric-value" style="font-size:19px;color:${on ? "var(--leaf)" : "var(--muted)"}">${pump ? (on ? "Đang chạy" : "Đang nghỉ") : "Chưa gán"}</p>
        <small class="muted">${pump ? escapeHtml(pump.assignment.deviceName || pump.assignment.deviceKey) : "Admin chưa gán máy bơm cho tài khoản"}</small>
      </div>
      <button class="switch ${on ? "on" : ""}" id="pumpSwitch" ${pump ? "" : "disabled"} aria-label="Bật tắt máy bơm"></button>
    </article>
  `;
}

function logbookCard() {
  const count = state.logbook?.records?.length || 0;
  return `
    <article class="wide-card card pad">
      <span class="icon-bubble" style="background:var(--info)">☷</span>
      <div class="grow">
        <h3>Logbook điện tử</h3>
        <p class="metric-value" style="font-size:19px;color:var(--info)">${count} dòng hôm nay</p>
        <small class="muted">Hiển thị min / max theo từng khung giờ</small>
      </div>
      <button class="btn icon" data-view="logbook">›</button>
    </article>
  `;
}

function devicesTemplate() {
  return `
    ${topbar("Thiết bị", "Chỉ hiển thị danh sách admin đã gán cho tài khoản")}
    ${devicesGrid(true)}
    <section class="section">
      <div class="section-head"><h2>Log máy bơm</h2><span class="muted">${state.pumpLogs.length} dòng</span></div>
      ${pumpLogsTable()}
    </section>
  `;
}

function devicesGrid(expanded = false) {
  const items = state.assigned.length ? state.assigned : [...state.sensors.keys()].map((key) => ({ deviceKey: key, deviceName: sensorName(key), deviceType: "sensor" }));
  if (!items.length) return `<div class="card empty">Chưa có thiết bị được gán. Vui lòng nhờ admin gán thiết bị cho tài khoản.</div>`;
  const visible = expanded ? items : items.slice(0, 4);
  return `<div class="grid four">${visible.map((item) => `
    <article class="device-tile card pad">
      <span class="icon-bubble">${isPump(item) ? "💧" : "⌁"}</span>
      <div>
        <h3>${escapeHtml(item.deviceName || item.deviceKey)}</h3>
        <small class="muted">${escapeHtml(item.deviceType || "device")} · ${escapeHtml(item.deviceKey)}</small>
      </div>
    </article>
  `).join("")}</div>`;
}

function logbookTemplate() {
  return `
    ${topbar("Logbook", "Giá trị thấp nhất và cao nhất theo giờ")}
    <div class="actions" style="justify-content:flex-start;margin-bottom:14px">
      <button class="btn primary" id="generateLogbook">Tạo lại hôm nay</button>
      <a class="btn" style="display:inline-flex;align-items:center;text-decoration:none" href="${state.apiBase}/logbooks/today/csv" target="_blank" rel="noreferrer">CSV</a>
    </div>
    ${logbookTable()}
  `;
}

function logbookTable() {
  const rows = state.logbook?.records || [];
  if (!rows.length) return `<div class="card empty">Chưa có dữ liệu logbook hôm nay.</div>`;
  return `
    <div class="table-wrap">
      <table>
        <thead><tr><th>Giờ</th><th>Thiết bị</th><th>Nhiệt min/max</th><th>Ẩm KK min/max</th><th>Không khí min/max</th><th>Đất min/max</th></tr></thead>
        <tbody>${rows.map((r) => `
          <tr>
            <td>${escapeHtml(r.periodStartLocal || r.localTime || "")}</td>
            <td>${escapeHtml(r.deviceName || r.deviceKey || "")}</td>
            <td>${fmt(r.minTemperature, "°C")} / ${fmt(r.maxTemperature, "°C")}</td>
            <td>${fmt(r.minHumidity, "%")} / ${fmt(r.maxHumidity, "%")}</td>
            <td>${fmt(r.minAirQuality, "ppm")} / ${fmt(r.maxAirQuality, "ppm")}</td>
            <td>${fmt(r.minSoilMoisture, "%")} / ${fmt(r.maxSoilMoisture, "%")}</td>
          </tr>
        `).join("")}</tbody>
      </table>
    </div>
  `;
}

function pumpLogsTable() {
  if (!state.pumpLogs.length) return `<div class="card empty">Chưa có log máy bơm.</div>`;
  return `
    <div class="table-wrap">
      <table>
        <thead><tr><th>Thời gian</th><th>Relay</th><th>Trạng thái</th><th>Nguồn</th><th>Người thao tác</th></tr></thead>
        <tbody>${state.pumpLogs.map((r) => `
          <tr><td>${escapeHtml(r.localTime || r.timestamp || "")}</td><td>${escapeHtml(r.relayKey || "")}</td><td>${r.value ? "Bật" : "Tắt"}</td><td>${escapeHtml(r.source || "")}</td><td>${escapeHtml(r.actorName || "")}</td></tr>
        `).join("")}</tbody>
      </table>
    </div>
  `;
}

function profileTemplate() {
  return `
    ${topbar("Tài khoản", "Thông tin phiên đăng nhập web")}
    <section class="card pad grid two">
      <div><span class="muted">Họ tên</span><h2>${escapeHtml(state.user?.fullName || "--")}</h2></div>
      <div><span class="muted">Vai trò</span><h2>${escapeHtml(state.user?.role || "--")}</h2></div>
      <div><span class="muted">Email</span><h2>${escapeHtml(state.user?.email || "--")}</h2></div>
      <div><span class="muted">Số điện thoại</span><h2>${escapeHtml(state.user?.phoneNumber || "--")}</h2></div>
    </section>
  `;
}

function detailTemplate() {
  const metric = metrics.find((x) => x.id === state.selectedMetric);
  const keys = [...state.sensors.keys()];
  const selected = state.selectedSensorKey || keys[0];
  state.selectedSensorKey = selected;
  const sensor = state.sensors.get(selected);
  const history = state.histories.get(selected) || [];
  const values = history.map((x) => historyValue(x, metric.id)).filter((x) => x !== null).map(Number);
  const current = valueFor(sensor, metric.id);
  return `
    <aside class="panel">
      <div class="topbar">
        <div><h1>${metric.label}</h1><div class="eyebrow">${escapeHtml(sensorName(selected) || "")}</div></div>
        <button class="btn icon" id="closeDrawer">×</button>
      </div>
      <section class="hero card" style="background:linear-gradient(135deg, ${metric.color}, #143b30);min-height:210px">
        <div class="hero-row"><span class="icon-bubble" style="background:rgba(255,255,255,.18)">${metric.icon}</span><span class="pill">${statusFor(metric.id, current)}</span></div>
        <div><h2>${fmt(current, metric.unit)}</h2><p>${escapeHtml(selected)}</p></div>
      </section>
      <section class="section">
        <div class="section-head"><h2>Cảm biến</h2><span class="muted">${keys.length} thiết bị</span></div>
        <div class="actions" style="justify-content:flex-start">${keys.map((key) => `<button class="btn ${key === selected ? "primary" : ""}" data-sensor="${escapeHtml(key)}">${escapeHtml(sensorName(key))}</button>`).join("")}</div>
      </section>
      <section class="section stat-strip">
        <div class="stat"><span class="muted">Thấp nhất</span><b>${fmt(values.length ? Math.min(...values) : null, metric.unit)}</b></div>
        <div class="stat"><span class="muted">Cao nhất</span><b>${fmt(values.length ? Math.max(...values) : null, metric.unit)}</b></div>
      </section>
      <section class="section card pad">
        <div class="section-head"><h2>Biểu đồ 24 giờ</h2><span class="muted">${history.length} mẫu</span></div>
        ${chartSvg(history, metric)}
      </section>
    </aside>
  `;
}

function chartSvg(history, metric) {
  const points = history.map((item) => ({ ts: parseTs(item.timestamp), value: historyValue(item, metric.id) }))
    .filter((x) => x.ts && x.value !== null)
    .sort((a, b) => a.ts - b.ts)
    .slice(-120);
  if (!points.length) return `<div class="chart empty">Đang thu thập dữ liệu...</div>`;
  const w = 720, h = 250, p = 28;
  const minY = metric.id === "temperature" ? 10 : 0;
  const maxY = Math.max(metric.max, ...points.map((x) => Number(x.value)));
  const minT = points[0].ts;
  const maxT = lastItem(points).ts || minT + 1;
  const xy = (pt) => {
    const x = p + ((pt.ts - minT) / Math.max(1, maxT - minT)) * (w - p * 2);
    const y = h - p - ((Number(pt.value) - minY) / Math.max(1, maxY - minY)) * (h - p * 2);
    return `${x.toFixed(1)},${y.toFixed(1)}`;
  };
  const poly = points.map(xy).join(" ");
  return `
    <svg class="chart" viewBox="0 0 ${w} ${h}" role="img" aria-label="Biểu đồ ${metric.label}">
      <polyline points="${poly}" fill="none" stroke="${metric.color}" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"></polyline>
      ${points.map((pt, i) => i % Math.ceil(points.length / 16) === 0 ? `<circle cx="${xy(pt).split(",")[0]}" cy="${xy(pt).split(",")[1]}" r="3" fill="${metric.color}"><title>${fmt(pt.value, metric.unit)}</title></circle>` : "").join("")}
    </svg>
  `;
}

function criticalMessage() {
  for (const sensor of state.sensors.values()) {
    const temp = valueFor(sensor, "temperature");
    const hum = valueFor(sensor, "humidity");
    const air = valueFor(sensor, "airQuality");
    const soil = valueFor(sensor, "soilMoisture");
    if (temp > 35) return "Nhiệt độ rất cao, cần kiểm tra nhà nấm ngay.";
    if (temp < 16) return "Nhiệt độ quá thấp, cần kiểm tra hệ thống.";
    if (hum !== null && (hum < 70 || hum > 96)) return "Độ ẩm không khí bất thường.";
    if (air > 1000) return "Chất lượng không khí rất xấu.";
    if (soil !== null && soil < 30) return "Độ ẩm đất thấp.";
  }
  return "";
}

function statusFor(kind, value) {
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

function latestUpdateLabel() {
  const stamps = [...state.sensors.values()].map((s) => parseTs(s.timestamp)).filter(Boolean);
  if (!stamps.length) return "Chưa có dữ liệu";
  const diff = Math.max(0, Date.now() - Math.max(...stamps));
  if (diff < 60000) return "vừa xong";
  if (diff < 3600000) return `${Math.floor(diff / 60000)} phút trước`;
  return `${Math.floor(diff / 3600000)} giờ trước`;
}

function isOnline(sensor) {
  const ts = parseTs(sensor?.timestamp);
  return ts && Math.abs(Date.now() - ts) <= 2 * 60 * 1000;
}

function parseTs(raw) {
  if (!raw) return null;
  const n = Number(raw);
  if (Number.isFinite(n)) return n < 1_000_000_000_000 ? n * 1000 : n;
  const parsed = Date.parse(String(raw).replace(" ", "T"));
  return Number.isFinite(parsed) ? parsed : null;
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#039;" }[c]));
}

function logout(renderNow = true) {
  storageRemove(TOKEN_KEY);
  state.token = "";
  state.user = null;
  if (renderNow) render();
}

function render() {
  if (!state.token) {
    app.innerHTML = loginTemplate();
    bindLogin();
    return;
  }
  const content = state.loading
    ? `${topbar("Đang tải", "Đang lấy dữ liệu từ backend")}<div class="card empty">Vui lòng chờ...</div>`
    : state.view === "devices"
      ? devicesTemplate()
      : state.view === "logbook"
        ? logbookTemplate()
        : state.view === "profile"
          ? profileTemplate()
          : homeTemplate();
  app.innerHTML = shellTemplate(content);
  bindApp();
}

function bindLogin() {
  document.querySelector("#loginForm")?.addEventListener("submit", async (event) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    state.apiBase = String(form.get("apiBase")).replace(/\/$/, "");
    storageSet(API_KEY, state.apiBase);
    state.error = "";
    try {
      const session = await api("/auth/login", {
        method: "POST",
        body: JSON.stringify({ identifier: form.get("identifier"), password: form.get("password") })
      });
      state.token = session.token;
      storageSet(TOKEN_KEY, state.token);
      await loadInitial();
    } catch (err) {
      state.error = err.message;
      render();
    }
  });
}

function bindApp() {
  document.querySelectorAll("[data-view]").forEach((el) => el.addEventListener("click", () => {
    state.view = el.dataset.view;
    state.selectedMetric = null;
    render();
  }));
  document.querySelector("#refreshBtn")?.addEventListener("click", refresh);
  document.querySelector("#logoutBtn")?.addEventListener("click", () => logout());
  document.querySelector("#pumpSwitch")?.addEventListener("click", togglePump);
  document.querySelector("#generateLogbook")?.addEventListener("click", async () => {
    state.logbook = await api("/logbooks/today/generate", { method: "POST" });
    render();
  });
  document.querySelectorAll("[data-metric]").forEach((el) => el.addEventListener("click", () => {
    state.selectedMetric = el.dataset.metric;
    state.selectedSensorKey = [...state.sensors.keys()][0] || null;
    render();
  }));
  document.querySelector("#closeDrawer")?.addEventListener("click", () => {
    state.selectedMetric = null;
    render();
  });
  document.querySelector("#drawer")?.addEventListener("click", (event) => {
    if (event.target.id === "drawer") {
      state.selectedMetric = null;
      render();
    }
  });
  document.querySelectorAll("[data-sensor]").forEach((el) => el.addEventListener("click", () => {
    state.selectedSensorKey = el.dataset.sensor;
    render();
  }));
}

render();
if (state.token) loadInitial();
