const state = {
  token: localStorage.getItem("mushtio_token") || "",
  user: null,
  view: "dashboard",
  summary: null,
  devices: [],
  sensorKeys: [],
  pumpKey: "",
  pump: null,
  schedule: null,
};

const $ = (id) => document.getElementById(id);
const apiHeaders = () => state.token ? { Authorization: `Bearer ${state.token}` } : {};

const viewTitles = {
  dashboard: "Dashboard",
  control: "Điều khiển máy bơm",
  devices: "Thiết bị",
  logbook: "Logbook",
  ai: "AI chat",
  profile: "Tài khoản",
  admin: "Admin",
};

async function api(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: {
      ...(options.body instanceof FormData ? {} : { "Content-Type": "application/json" }),
      ...apiHeaders(),
      ...(options.headers || {}),
    },
  });

  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`;
    try {
      const body = await response.json();
      message = body.message || body.title || message;
    } catch (_) {
      const text = await response.text().catch(() => "");
      if (text) message = text;
    }
    throw new Error(message);
  }

  const type = response.headers.get("content-type") || "";
  return type.includes("application/json") ? response.json() : response;
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function toast(message) {
  $("toast").textContent = message;
  $("toast").classList.remove("hidden");
  clearTimeout(window.__toastTimer);
  window.__toastTimer = setTimeout(() => $("toast").classList.add("hidden"), 3000);
}

function setBusy(button, busy) {
  if (!button) return;
  button.disabled = busy;
  if (busy) button.dataset.label = button.textContent;
  button.textContent = busy ? "Đang xử lý..." : button.dataset.label || button.textContent;
}

function showAuth(message = "") {
  $("auth-view").classList.remove("hidden");
  $("main-view").classList.add("hidden");
  $("auth-message").textContent = message;
}

function showApp() {
  $("auth-view").classList.add("hidden");
  $("main-view").classList.remove("hidden");
  $("user-pill").textContent = `${state.user?.fullName || "Người dùng"} - ${state.user?.role || "user"}`;
  document.querySelectorAll(".admin-only").forEach((el) => {
    el.classList.toggle("hidden", (state.user?.role || "").toLowerCase() !== "admin");
  });
  setView(state.view);
}

async function restore() {
  if (!state.token) {
    showAuth();
    return;
  }

  try {
    state.user = await api("/api/auth/me");
    showApp();
  } catch (_) {
    localStorage.removeItem("mushtio_token");
    state.token = "";
    showAuth();
  }
}

async function setView(view) {
  state.view = view;
  document.querySelectorAll(".nav").forEach((btn) => btn.classList.toggle("active", btn.dataset.view === view));
  document.querySelectorAll(".view").forEach((el) => el.classList.add("hidden"));
  $(`${view}-view`).classList.remove("hidden");
  $("page-title").textContent = viewTitles[view];

  const loaders = {
    dashboard: loadDashboard,
    control: loadControl,
    devices: loadDevices,
    logbook: loadLogbook,
    ai: loadAi,
    profile: loadProfile,
    admin: loadAdmin,
  };

  try {
    await loaders[view]();
  } catch (error) {
    $(`${view}-view`).innerHTML = `<article class="card"><h3>Không tải được dữ liệu</h3><p class="muted">${escapeHtml(error.message)}</p></article>`;
    toast(error.message || "Có lỗi xảy ra");
  }
}

function loadingCard(text = "Đang tải dữ liệu...") {
  return `<article class="card empty">${escapeHtml(text)}</article>`;
}

function metricColor(metric, fallback) {
  if (metric?.level === "danger") return "danger";
  if (metric?.level === "warning") return "warning";
  if (metric?.level === "muted") return "muted";
  return fallback;
}

function metricCard(title, metric, unit, icon, color) {
  const cssColor = metricColor(metric, color);
  return `<article class="card metric-card">
    <div class="metric-head">
      <div class="bubble" style="background: color-mix(in srgb, currentColor 16%, white); color: var(--${cssColor === "normal" ? "leaf" : cssColor});">${icon}</div>
      <span class="status-pill"><span class="dot"></span>${escapeHtml(metric?.status || "Chưa có dữ liệu")}</span>
    </div>
    <div>
      <div class="metric-value ${cssColor}">${escapeHtml(metric?.displayValue ?? "--")}${unit}</div>
      <p class="metric-title">${escapeHtml(title)}</p>
    </div>
  </article>`;
}

function hero(summary) {
  const hasAlert = Boolean(summary?.criticalMessage);
  return `<article class="card hero-card ${hasAlert ? "alert" : ""}">
    <div class="hero-row">
      <div class="bubble">M</div>
      <span class="status-pill"><span class="dot"></span>${hasAlert ? "Cần xử lý" : `${summary?.onlineCount ?? 0} online`}</span>
    </div>
    <div class="hero-title">${hasAlert ? "Có cảnh báo" : "Trang trại ổn định"}</div>
    <p class="hero-subtitle">${escapeHtml(summary?.criticalMessage || `${summary?.sensorCount ?? 0} cảm biến đang được theo dõi`)}</p>
    <div class="hero-chips">
      <div class="hero-chip"><span>Cảm biến</span><strong>${escapeHtml(summary?.sensorCount ?? 0)} thiết bị</strong></div>
      <div class="hero-chip"><span>Cập nhật</span><strong>${escapeHtml(summary?.latestUpdate || "Chưa có dữ liệu")}</strong></div>
    </div>
  </article>`;
}

async function loadDashboard() {
  $("dashboard-view").innerHTML = loadingCard();
  const [summary, devices] = await Promise.all([
    api("/api/sensors/summary"),
    fetchMyDevices(),
  ]);
  state.summary = summary;
  state.devices = devices;
  state.pumpKey = findPumpKey(devices);
  await loadPumpState(state.pumpKey);

  $("dashboard-view").innerHTML = `
    <div class="grid two-col">
      ${hero(summary)}
      ${pumpPreview()}
    </div>
    <div class="grid metrics" style="margin-top:14px">
      ${metricCard("Nhiệt độ", summary.temperature, "°C", "T", "coral")}
      ${metricCard("Độ ẩm không khí", summary.humidity, "%", "H", "sky")}
      ${metricCard("Không khí", summary.airQuality, "", "A", "leaf")}
      ${metricCard("Độ ẩm đất", summary.soilMoisture, "%", "S", "leaf")}
    </div>
    <div class="row-between" style="margin:20px 0 12px">
      <h2 style="margin:0">Thiết bị đang hoạt động</h2>
      <span class="muted">${devices.length} thiết bị</span>
    </div>
    <div class="grid cards">${deviceCards(devices.slice(0, 6))}</div>`;
}

function pumpPreview() {
  const isOn = Boolean(state.pump?.relay2 ?? state.pump?.relay1);
  const subtitle = state.pumpKey ? "Bấm để đổi trạng thái relay2" : "Tài khoản chưa được gán máy bơm";
  return `<article class="card hero-card" style="background:${isOn ? "linear-gradient(135deg, #0e3b2e, #20b26b)" : "linear-gradient(135deg, #1b2530, #51606d)"}">
    <div class="hero-row">
      <div class="bubble">P</div>
      <button class="switch ${isOn ? "on" : ""}" type="button" onclick="toggleActivePump()" ${state.pumpKey ? "" : "disabled"} aria-label="Bật tắt máy bơm"><span></span></button>
    </div>
    <div class="hero-title">${isOn ? "Máy bơm ON" : "Máy bơm OFF"}</div>
    <p class="hero-subtitle">${escapeHtml(subtitle)}</p>
    <div class="hero-chips">
      <div class="hero-chip"><span>Thiết bị</span><strong>${escapeHtml(state.pumpKey || "--")}</strong></div>
      <div class="hero-chip"><span>Thao tác cuối</span><strong>${escapeHtml(state.pump?.lastActionLocal || "Chưa có dữ liệu")}</strong></div>
    </div>
  </article>`;
}

async function fetchMyDevices() {
  try {
    return await api("/api/me/devices");
  } catch (_) {
    return [];
  }
}

function findPumpKey(devices) {
  const pump = devices.find((d) => `${d.deviceType} ${d.deviceName} ${d.deviceKey}`.toLowerCase().includes("pump")
    || `${d.deviceType} ${d.deviceName}`.toLowerCase().includes("bơm")
    || `${d.deviceType} ${d.deviceName}`.toLowerCase().includes("bom"));
  return pump?.deviceKey || devices[0]?.deviceKey || "pump_1";
}

async function loadPumpState(pumpKey) {
  state.pump = null;
  if (!pumpKey) return;
  try {
    state.pump = await api(`/api/devices/${encodeURIComponent(pumpKey)}`);
  } catch (_) {
    state.pump = null;
  }
}

function deviceCards(devices) {
  if (!devices.length) {
    return `<article class="card empty">Chưa có thiết bị được gán cho tài khoản này.</article>`;
  }

  return devices.map((device) => `<article class="card">
    <div class="metric-head">
      <div class="bubble" style="background:rgba(32,178,107,.12); color:var(--leaf)">${deviceIcon(device.deviceType)}</div>
      <span class="status-pill"><span class="dot"></span>${escapeHtml(device.deviceType || "device")}</span>
    </div>
    <h3 style="margin-top:18px">${escapeHtml(device.deviceName || device.deviceKey)}</h3>
    <p class="muted">${escapeHtml(device.deviceKey)}</p>
  </article>`).join("");
}

function deviceIcon(type = "") {
  const lower = type.toLowerCase();
  if (lower.includes("pump") || lower.includes("bom") || lower.includes("bơm")) return "P";
  if (lower.includes("sensor")) return "S";
  return "D";
}

async function loadControl() {
  $("control-view").innerHTML = loadingCard();
  const devices = state.devices.length ? state.devices : await fetchMyDevices();
  state.devices = devices;
  state.pumpKey = findPumpKey(devices);
  await Promise.all([loadPumpState(state.pumpKey), loadSchedule()]);

  const isOn = Boolean(state.pump?.relay2 ?? state.pump?.relay1);
  $("control-view").innerHTML = `
    <div class="grid two-col">
      ${pumpPreview()}
      <article class="card">
        <div class="row-between">
          <div>
            <h3>Lịch tưới thông minh</h3>
            <p class="muted">Lưu cấu hình tự tưới qua API backend.</p>
          </div>
          <span class="status-pill"><span class="dot"></span>${state.schedule?.enabled ? "Đang bật" : "Đang tắt"}</span>
        </div>
        ${scheduleForm()}
      </article>
    </div>
    <article class="card" style="margin-top:14px">
      <div class="row-between">
        <h3>Lịch sử hoạt động</h3>
        <button class="secondary" type="button" onclick="loadControl()">Tải lại</button>
      </div>
      <div id="pump-logs" style="margin-top:12px">${loadingCard("Đang tải lịch sử...")}</div>
    </article>`;

  $("schedule-form").onsubmit = saveSchedule;
  loadPumpLogs();
  if (!isOn && !state.pumpKey) toast("Chưa tìm thấy máy bơm được gán cho tài khoản.");
}

async function loadSchedule() {
  state.schedule = null;
  if (!state.pumpKey) return;
  try {
    state.schedule = await api(`/api/devices/${encodeURIComponent(state.pumpKey)}/schedule/relay2`);
  } catch (_) {
    state.schedule = null;
  }
}

function scheduleForm() {
  const s = state.schedule || {};
  return `<form id="schedule-form" class="form" style="margin-top:16px">
    <label><span><input name="enabled" type="checkbox" ${s.enabled ? "checked" : ""} style="width:auto; min-height:0"> Bật lịch tưới</span></label>
    <div class="split-form">
      <label>Chu kỳ (phút)<input name="intervalMinutes" type="number" min="1" value="${escapeHtml(s.intervalMinutes || 180)}" required></label>
      <label>Thời lượng (phút)<input name="durationMinutes" type="number" min="1" value="${escapeHtml(s.durationMinutes || 10)}" required></label>
    </div>
    <label>Giờ bắt đầu<input name="startTime" pattern="^([01][0-9]|2[0-3]):[0-5][0-9]$" value="${escapeHtml(s.startTime || "06:00")}" required></label>
    <label><span><input name="smartEnabled" type="checkbox" ${s.smartEnabled ? "checked" : ""} style="width:auto; min-height:0"> Bật tưới theo ngưỡng độ ẩm đất</span></label>
    <div class="split-form">
      <label>Sensor độ ẩm đất<input name="sensorKey" value="${escapeHtml(s.sensorKey || "")}" placeholder="VD: sensor_1"></label>
      <label>Ngưỡng đất (%)<input name="soilMoistureThreshold" type="number" min="1" step="0.1" value="${escapeHtml(s.soilMoistureThreshold || 30)}"></label>
    </div>
    <div class="split-form">
      <label>Tối đa (phút)<input name="maxDurationMinutes" type="number" min="1" value="${escapeHtml(s.maxDurationMinutes || 10)}"></label>
      <label>Nghỉ (phút)<input name="cooldownMinutes" type="number" min="1" value="${escapeHtml(s.cooldownMinutes || 30)}"></label>
    </div>
    <button class="primary" type="submit">Lưu lịch tưới</button>
  </form>`;
}

async function loadPumpLogs() {
  if (!state.pumpKey) {
    $("pump-logs").innerHTML = `<div class="empty">Chưa có máy bơm.</div>`;
    return;
  }
  const logs = await api(`/api/devices/${encodeURIComponent(state.pumpKey)}/logs?limit=50`);
  $("pump-logs").innerHTML = logs.length ? `<div class="table-wrap"><table>
    <thead><tr><th>Thời gian</th><th>Trạng thái</th><th>Nguồn</th><th>Người thao tác</th></tr></thead>
    <tbody>${logs.map((log) => `<tr>
      <td>${escapeHtml(log.localTime || log.utcTime || "")}</td>
      <td>${log.value ? "Bật" : "Tắt"}</td>
      <td>${escapeHtml(sourceLabel(log.source))}</td>
      <td>${escapeHtml(log.actorName || "")}</td>
    </tr>`).join("")}</tbody>
  </table></div>` : `<div class="empty">Chưa có lịch sử hoạt động.</div>`;
}

async function toggleActivePump() {
  if (!state.pumpKey) return;
  const next = !Boolean(state.pump?.relay2 ?? state.pump?.relay1);
  await setRelay(state.pumpKey, next, true);
}

async function setRelay(deviceKey, value, refreshCurrent = false) {
  await api(`/api/devices/${encodeURIComponent(deviceKey)}/relay/relay2`, {
    method: "PUT",
    body: JSON.stringify({ value }),
  });
  toast(value ? "Đã bật máy bơm" : "Đã tắt máy bơm");
  if (refreshCurrent) setView(state.view);
}

async function saveSchedule(event) {
  event.preventDefault();
  const button = event.submitter;
  setBusy(button, true);
  try {
    const data = Object.fromEntries(new FormData(event.currentTarget));
    const payload = {
      enabled: data.enabled === "on",
      intervalMinutes: Number(data.intervalMinutes),
      durationMinutes: Number(data.durationMinutes),
      startTime: data.startTime,
      smartEnabled: data.smartEnabled === "on",
      sensorKey: data.sensorKey?.trim() || null,
      soilMoistureThreshold: Number(data.soilMoistureThreshold || 30),
      maxDurationMinutes: Number(data.maxDurationMinutes || 10),
      cooldownMinutes: Number(data.cooldownMinutes || 30),
    };
    state.schedule = await api(`/api/devices/${encodeURIComponent(state.pumpKey)}/schedule/relay2`, {
      method: "PUT",
      body: JSON.stringify(payload),
    });
    toast("Đã lưu lịch tưới");
    setView("control");
  } finally {
    setBusy(button, false);
  }
}

function sourceLabel(source) {
  if (source === "schedule") return "Lịch tự động";
  if (source === "smart-threshold") return "Ngưỡng độ ẩm";
  return "Thủ công";
}

async function loadDevices() {
  $("devices-view").innerHTML = loadingCard();
  const [assigned, sensorKeys] = await Promise.all([fetchMyDevices(), api("/api/sensors")]);
  state.devices = assigned;
  state.sensorKeys = sensorKeys;
  $("devices-view").innerHTML = `
    <div class="grid cards">${deviceCards(assigned)}</div>
    <article class="card" style="margin-top:14px">
      <h3>Cảm biến Firebase</h3>
      <p class="muted">Danh sách lấy từ /api/sensors.</p>
      <div class="table-wrap" style="margin-top:12px"><table>
        <thead><tr><th>Sensor key</th><th>Thao tác</th></tr></thead>
        <tbody>${sensorKeys.map((key) => `<tr><td>${escapeHtml(key)}</td><td><button class="secondary" type="button" onclick="loadSensorDetail('${escapeHtml(key)}')">Xem dữ liệu</button></td></tr>`).join("")}</tbody>
      </table></div>
    </article>
    <div id="sensor-detail" style="margin-top:14px"></div>`;
}

async function loadSensorDetail(sensorKey) {
  const sensor = await api(`/api/sensors/${encodeURIComponent(sensorKey)}`);
  $("sensor-detail").innerHTML = `<article class="card">
    <div class="row-between"><h3>${escapeHtml(sensor.deviceName || sensorKey)}</h3><span class="status-pill"><span class="dot"></span>${escapeHtml(sensor.timestamp || "Realtime")}</span></div>
    <div class="grid metrics" style="margin-top:14px">
      ${simpleMetric("Nhiệt độ", sensor.temperature, "°C", "coral")}
      ${simpleMetric("Độ ẩm", sensor.humidity, "%", "sky")}
      ${simpleMetric("Không khí", sensor.airQuality, "", "leaf")}
      ${simpleMetric("Độ ẩm đất", sensor.soilMoisture, "%", "leaf")}
    </div>
  </article>`;
}

function simpleMetric(title, value, unit, color) {
  return `<article class="card metric-card"><div><p class="metric-title">${escapeHtml(title)}</p><div class="metric-value ${color}">${value ?? "--"}${unit}</div></div></article>`;
}

async function loadLogbook() {
  $("logbook-view").innerHTML = loadingCard();
  const logbook = await api("/api/logbooks/today");
  const rows = (logbook.records || []).map((r) => `<tr>
    <td>${escapeHtml(r.localTime || r.periodStartLocal || "")}</td>
    <td>${escapeHtml(r.deviceName || r.deviceKey || "")}</td>
    <td>${r.temperature ?? ""}</td>
    <td>${r.humidity ?? ""}</td>
    <td>${r.airQuality ?? ""}</td>
    <td>${r.soilMoisture ?? ""}</td>
  </tr>`).join("");

  $("logbook-view").innerHTML = `
    <article class="wide-card">
      <div class="bubble" style="background:rgba(47,128,237,.12); color:var(--sky)">L</div>
      <div class="grow">
        <h3>Logbook ${escapeHtml(logbook.date)}</h3>
        <p class="muted">Tạo lúc ${escapeHtml(logbook.generatedLocal || "")}. Backend tổng hợp ${logbook.records?.length || 0} dòng.</p>
      </div>
      <div class="actions">
        <button class="primary" type="button" onclick="downloadLogbook()">Tải CSV</button>
        <button class="secondary" type="button" onclick="regenerateLogbook()">Tạo lại</button>
      </div>
    </article>
    <div class="table-wrap" style="margin-top:14px"><table>
      <thead><tr><th>Thời gian</th><th>Thiết bị</th><th>Nhiệt độ</th><th>Độ ẩm</th><th>Không khí</th><th>Độ ẩm đất</th></tr></thead>
      <tbody>${rows || `<tr><td colspan="6" class="empty">Chưa có dữ liệu logbook.</td></tr>`}</tbody>
    </table></div>`;
}

function downloadLogbook() {
  window.location.href = "/api/logbooks/today/csv";
}

async function regenerateLogbook() {
  await api("/api/logbooks/today/generate", { method: "POST" });
  toast("Đã tạo lại logbook");
  loadLogbook();
}

function loadAi() {
  $("ai-view").innerHTML = `<div class="grid two-col">
    <article class="card">
      <h3>AI kiểm tra trang trại nấm</h3>
      <p class="muted">Gửi câu hỏi và ảnh để backend gọi GeminiService.</p>
      <form id="ai-form" class="form" style="margin-top:16px">
        <label>Câu hỏi<textarea name="message" required placeholder="Hỏi về tình trạng trang trại nấm..."></textarea></label>
        <label>Hình ảnh<input name="image" type="file" accept="image/*"></label>
        <button class="primary" type="submit">Gửi AI</button>
      </form>
    </article>
    <article id="ai-answer" class="card"><h3>Phản hồi</h3><p class="muted">Câu trả lời sẽ hiển thị ở đây.</p></article>
  </div>`;
  $("ai-form").onsubmit = sendAi;
}

async function sendAi(event) {
  event.preventDefault();
  const button = event.submitter;
  setBusy(button, true);
  try {
    const data = new FormData(event.currentTarget);
    data.set("userId", state.user?.userId || "");
    const file = data.get("image");
    if (!file || file.size === 0) data.delete("image");
    $("ai-answer").innerHTML = `<h3>Phản hồi</h3><p class="muted">Đang xử lý...</p>`;
    const result = await api("/api/ai/chat", { method: "POST", body: data });
    $("ai-answer").innerHTML = `<h3>Phản hồi</h3><p style="white-space:pre-wrap">${escapeHtml(result.answer || "Không có phản hồi.")}</p>`;
  } finally {
    setBusy(button, false);
  }
}

async function loadProfile() {
  const [profile, account, activities] = await Promise.all([
    api("/api/auth/me"),
    api("/api/auth/account").catch(() => null),
    api("/api/auth/activities?limit=20").catch(() => []),
  ]);
  state.user = profile;
  $("profile-view").innerHTML = `
    <div class="grid two-col">
      <article class="card">
        <h3>Thông tin tài khoản</h3>
        <form id="profile-form" class="form" style="margin-top:16px">
          <label>Họ tên<input name="fullName" value="${escapeHtml(profile.fullName)}" required></label>
          <label>Số điện thoại<input name="phoneNumber" value="${escapeHtml(profile.phoneNumber)}" required></label>
          <label>Email<input name="email" type="email" value="${escapeHtml(profile.email)}" required></label>
          <label>Địa chỉ<input name="address" value="${escapeHtml(profile.address)}" required></label>
          <label>Ngày sinh<input name="dateOfBirth" type="date" value="${escapeHtml((profile.dateOfBirth || "").slice(0, 10))}" required></label>
          <button class="primary" type="submit">Lưu hồ sơ</button>
        </form>
      </article>
      <article class="card">
        <h3>Tổng quan</h3>
        <div class="grid cards" style="grid-template-columns:1fr; margin-top:14px">
          ${simpleInfo("Vai trò", profile.role)}
          ${simpleInfo("Thiết bị", account?.assignedDeviceCount ?? state.devices.length)}
          ${simpleInfo("Hoạt động gần đây", activities.length)}
        </div>
      </article>
    </div>
    <article class="card" style="margin-top:14px">
      <h3>Lịch sử hoạt động</h3>
      <div class="table-wrap" style="margin-top:12px"><table>
        <thead><tr><th>Thời gian</th><th>Hoạt động</th></tr></thead>
        <tbody>${activities.map((a) => `<tr><td>${escapeHtml(a.localTime || a.createdAt || "")}</td><td>${escapeHtml(a.description || a.action || "")}</td></tr>`).join("") || `<tr><td colspan="2" class="empty">Chưa có hoạt động.</td></tr>`}</tbody>
      </table></div>
    </article>`;
  $("profile-form").onsubmit = saveProfile;
}

function simpleInfo(label, value) {
  return `<div class="wide-card"><div class="grow"><p class="muted">${escapeHtml(label)}</p><h3>${escapeHtml(value ?? "--")}</h3></div></div>`;
}

async function saveProfile(event) {
  event.preventDefault();
  const button = event.submitter;
  setBusy(button, true);
  try {
    const data = Object.fromEntries(new FormData(event.currentTarget));
    state.user = await api("/api/auth/me", { method: "PUT", body: JSON.stringify(data) });
    $("user-pill").textContent = `${state.user.fullName} - ${state.user.role}`;
    toast("Đã cập nhật hồ sơ");
  } finally {
    setBusy(button, false);
  }
}

async function loadAdmin() {
  if ((state.user?.role || "").toLowerCase() !== "admin") {
    $("admin-view").innerHTML = `<article class="card">Bạn không có quyền admin.</article>`;
    return;
  }

  $("admin-view").innerHTML = loadingCard();
  const [stats, users, devices] = await Promise.all([
    api("/api/admin/stats"),
    api("/api/admin/users"),
    api("/api/admin/firebase-devices"),
  ]);
  $("admin-view").innerHTML = `
    <div class="grid metrics">
      ${simpleMetric("Người dùng", stats.totalUsers ?? users.length, "", "leaf")}
      ${simpleMetric("Thiết bị Firebase", devices.length, "", "sky")}
      ${simpleMetric("Online", stats.onlineDevices ?? "--", "", "violet")}
      ${simpleMetric("Offline", stats.offlineDevices ?? "--", "", "coral")}
    </div>
    <div class="grid two-col" style="margin-top:14px">
      <article class="card">
        <h3>Người dùng</h3>
        <div class="table-wrap" style="margin-top:12px"><table>
          <thead><tr><th>Tên</th><th>Phone</th><th>Role</th></tr></thead>
          <tbody>${users.map((u) => `<tr><td>${escapeHtml(u.fullName)}</td><td>${escapeHtml(u.phoneNumber)}</td><td>${escapeHtml(u.role)}</td></tr>`).join("")}</tbody>
        </table></div>
      </article>
      <article class="card">
        <h3>Thiết bị Firebase</h3>
        <div class="table-wrap" style="margin-top:12px"><table>
          <thead><tr><th>Key</th><th>Tên</th><th>Loại</th></tr></thead>
          <tbody>${devices.map((d) => `<tr><td>${escapeHtml(d.deviceKey)}</td><td>${escapeHtml(d.deviceName)}</td><td>${escapeHtml(d.deviceType)}</td></tr>`).join("")}</tbody>
        </table></div>
      </article>
    </div>`;
}

function wireAuth() {
  $("login-tab").onclick = () => switchAuthTab("login");
  $("register-tab").onclick = () => switchAuthTab("register");

  $("login-form").onsubmit = async (event) => {
    event.preventDefault();
    const button = event.submitter;
    setBusy(button, true);
    $("auth-message").textContent = "";
    try {
      const data = Object.fromEntries(new FormData(event.currentTarget));
      const session = await api("/api/auth/login", { method: "POST", body: JSON.stringify(data) });
      state.token = session.token;
      state.user = session.user;
      localStorage.setItem("mushtio_token", state.token);
      showApp();
    } catch (error) {
      $("auth-message").textContent = error.message || "Đăng nhập thất bại";
    } finally {
      setBusy(button, false);
    }
  };

  $("send-code-btn").onclick = async (event) => {
    const button = event.currentTarget;
    setBusy(button, true);
    try {
      const email = new FormData($("register-form")).get("email");
      await api("/api/auth/request-email-code", { method: "POST", body: JSON.stringify({ email, purpose: "register" }) });
      toast("Đã gửi OTP");
    } finally {
      setBusy(button, false);
    }
  };

  $("register-form").onsubmit = async (event) => {
    event.preventDefault();
    const button = event.submitter;
    setBusy(button, true);
    $("auth-message").textContent = "";
    try {
      const data = Object.fromEntries(new FormData(event.currentTarget));
      await api("/api/auth/verify-email-code", {
        method: "POST",
        body: JSON.stringify({ email: data.email, code: data.code, purpose: "register" }),
      });
      delete data.code;
      const session = await api("/api/auth/register", { method: "POST", body: JSON.stringify(data) });
      state.token = session.token;
      state.user = session.user;
      localStorage.setItem("mushtio_token", state.token);
      showApp();
    } catch (error) {
      $("auth-message").textContent = error.message || "Đăng ký thất bại";
    } finally {
      setBusy(button, false);
    }
  };
}

function switchAuthTab(tab) {
  $("login-tab").classList.toggle("active", tab === "login");
  $("register-tab").classList.toggle("active", tab === "register");
  $("login-form").classList.toggle("hidden", tab !== "login");
  $("register-form").classList.toggle("hidden", tab !== "register");
  $("auth-message").textContent = "";
}

document.addEventListener("click", (event) => {
  const nav = event.target.closest(".nav");
  if (nav) setView(nav.dataset.view);
});

$("refresh-btn").onclick = () => setView(state.view);
$("logout-btn").onclick = async () => {
  try { await api("/api/auth/logout", { method: "POST" }); } catch (_) {}
  localStorage.removeItem("mushtio_token");
  state.token = "";
  state.user = null;
  state.view = "dashboard";
  showAuth();
};

window.addEventListener("error", (event) => toast(event.message));
window.addEventListener("unhandledrejection", (event) => toast(event.reason?.message || "Có lỗi xảy ra"));

wireAuth();
restore();
