import {
  activateSkillMessage,
  cancelOrderMessage,
  debugAdvanceStageMessage,
  debugGiveCardMessage,
  debugInjectNewsMessage,
  debugQueryMessage,
  debugSetPlayerMessage,
  helloMessage,
  limitBuyMessage,
  limitSellMessage,
  selectStrategyMessage,
  sendJson,
  submitReportMessage,
} from "./actions.js";
import { buildSampleMessages } from "./sample-data.js";
import {
  applyMessage,
  clearSettlement,
  createInitialState,
  markNewsAsRead,
  pushEvent,
  resetUiCollections,
  routeFromLocation,
  setActiveView,
  setCandleOptions,
  setColorScheme,
  setConnectionPatch,
  setMode,
} from "./store.js";
import {
  handleMarketChartPointerDown,
  handleMarketChartPointerMove,
  handleMarketChartPointerUp,
  handleMarketChartWheel,
  renderApp,
  resetMarketChartViewport,
} from "./render.js";
import { applyColorScheme, loadColorScheme, saveColorScheme } from "./appearance.js";

const state = createInitialState(routeFromLocation(window.location));
setColorScheme(state, loadColorScheme());
applyColorScheme(state.ui.colorScheme);
let ws = null;
let reconnectTimer = null;
let manuallyClosed = false;
let marketCanvasHandlersBound = false;

initParticles();
bindControls();
bindMarketChartControls();
renderApp(state);

function bindControls() {
  document.getElementById("modeTabs")?.addEventListener("click", (event) => {
    const button = event.target.closest("[data-mode]");
    if (!button) return;
    setMode(state, button.dataset.mode);
    updateRoute();
    renderApp(state);
  });

  document.querySelector(".menu-panel")?.addEventListener("click", (event) => {
    const button = event.target.closest("[data-view]");
    if (!button) return;
    if (state.connection.role !== "player" && (button.dataset.view === "info" || button.dataset.view === "debug")) {
      return;
    }
    if (state.connection.role === "player" && button.dataset.view === "server-debug") {
      return;
    }
    setActiveView(state, button.dataset.view);
    renderApp(state);
  });

  document.getElementById("dailySummary")?.addEventListener("pointerdown", handleSummaryInteraction, true);
  document.getElementById("dailySummary")?.addEventListener("click", handleSummaryInteraction, true);

  document.body.addEventListener("click", (event) => {
    const openButton = event.target.closest("[data-open-modal]");
    if (openButton) {
      if (openButton.dataset.openModal === "newsModal") {
        markNewsAsRead(state);
      }
      openModal(openButton.dataset.openModal);
      renderApp(state);
      return;
    }

    const closeButton = event.target.closest("[data-close-modal]");
    if (closeButton) {
      closeModal(closeButton.dataset.closeModal);
      return;
    }

    const playerButton = event.target.closest("[data-player-token]");
    if (playerButton) {
      showPlayerDetail(playerButton.dataset.playerToken);
    }
  });

  document.getElementById("portInput")?.addEventListener("change", (event) => {
    const port = event.target.value.trim() || "14514";
    setConnectionPatch(state, { server: `ws://localhost:${port}` });
    updateRoute();
    renderApp(state);
  });

  document.getElementById("tokenInput")?.addEventListener("change", (event) => {
    setConnectionPatch(state, { token: event.target.value.trim() || "player1" });
    updateRoute();
    renderApp(state);
  });

  document.getElementById("connectButton")?.addEventListener("click", connect);
  document.getElementById("disconnectButton")?.addEventListener("click", () => disconnect(true));
  document.getElementById("demoButton")?.addEventListener("click", loadDemo);

  document.getElementById("priceModeSelect")?.addEventListener("change", (event) => {
    setCandleOptions(state, { priceField: event.target.value });
    resetMarketChartViewport();
    renderApp(state);
  });

  document.getElementById("intervalSelect")?.addEventListener("change", (event) => {
    setCandleOptions(state, { interval: event.target.value });
    resetMarketChartViewport();
    renderApp(state);
  });

  document.getElementById("colorSchemeSelect")?.addEventListener("change", (event) => {
    setColorScheme(state, event.target.value);
    saveColorScheme(state.ui.colorScheme);
    renderApp(state);
  });

  document.getElementById("orderForm")?.addEventListener("submit", handleOrder);
  document.getElementById("cancelForm")?.addEventListener("submit", handleCancel);
  document.getElementById("reportForm")?.addEventListener("submit", handleReport);
  document.getElementById("quickReportForm")?.addEventListener("submit", handleQuickReport);
  document.getElementById("skillForm")?.addEventListener("submit", handleSkill);

  document.getElementById("serverOrderForm")?.addEventListener("submit", handleServerOrder);
  document.getElementById("serverCancelForm")?.addEventListener("submit", handleServerCancel);
  document.getElementById("serverReportForm")?.addEventListener("submit", handleServerReport);
  document.getElementById("serverStrategyForm")?.addEventListener("submit", handleServerStrategy);
  document.getElementById("serverSkillForm")?.addEventListener("submit", handleServerSkill);
  document.getElementById("serverRawForm")?.addEventListener("submit", handleServerRaw);
  document.getElementById("debugQueryForm")?.addEventListener("submit", handleDebugQuery);
  document.getElementById("debugAdvanceForm")?.addEventListener("submit", handleDebugAdvance);
  document.getElementById("debugGiveCardForm")?.addEventListener("submit", handleDebugGiveCard);
  document.getElementById("debugInjectNewsForm")?.addEventListener("submit", handleDebugInjectNews);
  document.getElementById("debugSetPlayerForm")?.addEventListener("submit", handleDebugSetPlayer);

  document.getElementById("strategyOptions")?.addEventListener("click", (event) => {
    const button = event.target.closest("[data-action='select-strategy']");
    if (!button) return;
    sendAction(selectStrategyMessage(state.connection.token, button.dataset.cardName));
  });

  document.getElementById("closeSettlementButton")?.addEventListener("click", () => {
    clearSettlement(state);
    renderApp(state);
  });

  window.addEventListener("resize", () => renderApp(state));
}

function bindMarketChartControls() {
  if (marketCanvasHandlersBound) return;
  const canvas = document.getElementById("marketCanvas");
  if (!canvas) return;
  marketCanvasHandlersBound = true;

  canvas.addEventListener("pointerdown", (event) => {
    handleMarketChartPointerDown(canvas, event, state);
  });
  canvas.addEventListener("pointermove", (event) => {
    if (handleMarketChartPointerMove(canvas, event, state)) {
      renderApp(state);
    }
  });
  canvas.addEventListener("pointerup", (event) => {
    handleMarketChartPointerUp(canvas, event);
    renderApp(state);
  });
  canvas.addEventListener("pointercancel", (event) => {
    handleMarketChartPointerUp(canvas, event);
    renderApp(state);
  });
  canvas.addEventListener("wheel", (event) => {
    if (handleMarketChartWheel(canvas, event, state)) {
      renderApp(state);
    }
  }, { passive: false });
}

function handleSummaryInteraction(event) {
  const button = event.target.closest("[data-action='open-summary']");
  if (!button) return;
  if (event.type === "pointerdown") {
    event.preventDefault();
  }
  showSummaryDetail(button.dataset.summaryDay);
}

function serverUrlFromPort() {
  const port = document.getElementById("portInput")?.value.trim() || "14514";
  return `ws://localhost:${port}`;
}

function connect() {
  clearTimeout(reconnectTimer);
  manuallyClosed = false;

  if (ws && ws.readyState <= 1) {
    ws.close();
  }

  const server = serverUrlFromPort();
  const token = document.getElementById("tokenInput")?.value.trim() || state.connection.token;
  setConnectionPatch(state, {
    server,
    token,
    status: "connecting",
    lastError: "",
  });
  updateRoute();
  renderApp(state);

  try {
    ws = new WebSocket(server);
  } catch (error) {
    handleSocketError(error);
    return;
  }

  ws.addEventListener("open", () => {
    setConnectionPatch(state, {
      status: "connected",
      reconnectAttempt: 0,
      lastError: "",
    });
    sendAction(
      helloMessage(state.connection.role, state.connection.token, state.connection.adminSecret),
      { silent: true },
    );
    if (state.connection.role === "player" && state.connection.token) {
      sendAction(cancelOrderMessage(state.connection.token, -1), { silent: true });
    }
    renderApp(state);
  });

  ws.addEventListener("message", (event) => {
    try {
      applyMessage(state, JSON.parse(event.data));
    } catch (error) {
      pushEvent(state, {
        kind: "error",
        title: "消息解析失败",
        detail: error.message,
      });
    }
    renderApp(state);
  });

  ws.addEventListener("error", () => {
    handleSocketError(new Error("WebSocket error"));
  });

  ws.addEventListener("close", () => {
    const nextStatus = manuallyClosed ? "disconnected" : "error";
    setConnectionPatch(state, { status: nextStatus });
    renderApp(state);
    if (!manuallyClosed) scheduleReconnect();
  });
}

function disconnect(byUser) {
  manuallyClosed = byUser;
  clearTimeout(reconnectTimer);
  if (ws && ws.readyState <= 1) {
    ws.close();
  }
  setConnectionPatch(state, { status: "disconnected" });
  renderApp(state);
}

function scheduleReconnect() {
  const attempt = state.connection.reconnectAttempt + 1;
  const delay = Math.min(10000, 500 * 2 ** Math.min(attempt, 5));
  setConnectionPatch(state, { reconnectAttempt: attempt });
  reconnectTimer = window.setTimeout(connect, delay);
}

function handleSocketError(error) {
  setConnectionPatch(state, {
    status: "error",
    lastError: error.message,
  });
  pushEvent(state, {
    kind: "error",
    title: "连接错误",
    detail: error.message,
  });
  renderApp(state);
}

function sendAction(message, options = {}) {
  try {
    sendJson(ws, message);
    setConnectionPatch(state, { lastSent: message.messageType });
    if (!options.silent) {
      pushEvent(state, {
        kind: "system",
        title: `已发送 ${message.messageType}`,
        detail: actionDetail(message),
      });
    }
  } catch (error) {
    applyMessage(state, {
      messageType: "ERROR",
      errorCode: 1000,
      message: error.message,
    });
  }
  renderApp(state);
}

function handleOrder(event) {
  event.preventDefault();
  const submitter = event.submitter;
  const data = new FormData(event.currentTarget);
  const price = data.get("price");
  const quantity = data.get("quantity");
  const token = state.connection.token;
  const message = submitter?.value === "sell"
    ? limitSellMessage(token, price, quantity)
    : limitBuyMessage(token, price, quantity);
  sendAction(message);
}

function handleCancel(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(cancelOrderMessage(state.connection.token, data.get("orderId")));
}

function handleReport(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(submitReportMessage(
    state.connection.token,
    data.get("newsId"),
    data.get("prediction"),
  ));
}

function handleQuickReport(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  const prediction = event.submitter?.value || data.get("prediction");
  sendAction(submitReportMessage(
    state.connection.token,
    data.get("newsId"),
    prediction,
  ));
}

function handleSkill(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(activateSkillMessage(
    state.connection.token,
    String(data.get("skillName") || "").trim(),
    data.get("direction"),
  ));
}

function serverDebugToken() {
  return document.getElementById("serverDebugToken")?.value.trim() || state.connection.token;
}

function handleServerOrder(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  const message = event.submitter?.value === "sell"
    ? limitSellMessage(serverDebugToken(), data.get("price"), data.get("quantity"))
    : limitBuyMessage(serverDebugToken(), data.get("price"), data.get("quantity"));
  sendAction(message);
}

function handleServerCancel(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(cancelOrderMessage(serverDebugToken(), data.get("orderId")));
}

function handleServerReport(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(submitReportMessage(serverDebugToken(), data.get("newsId"), data.get("prediction")));
}

function handleServerStrategy(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(selectStrategyMessage(serverDebugToken(), data.get("cardName")));
}

function handleServerSkill(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(activateSkillMessage(serverDebugToken(), String(data.get("skillName") || "").trim(), data.get("direction")));
}

function handleServerRaw(event) {
  event.preventDefault();
  const raw = document.getElementById("serverRawJson")?.value.trim();
  if (!raw) return;
  let message;
  try {
    message = JSON.parse(raw);
  } catch {
    applyMessage(state, { messageType: "ERROR", errorCode: 1000, message: "JSON 解析失败" });
    renderApp(state);
    return;
  }
  sendAction(message);
}

function handleDebugQuery(event) {
  event.preventDefault();
  sendAction(debugQueryMessage());
}

function handleDebugAdvance(event) {
  event.preventDefault();
  sendAction(debugAdvanceStageMessage());
}

function handleDebugGiveCard(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(debugGiveCardMessage(
    String(data.get("targetToken") || "").trim(),
    String(data.get("cardName") || "").trim(),
  ));
}

function handleDebugInjectNews(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(debugInjectNewsMessage(
    String(data.get("sentiment") || "Bullish"),
    String(data.get("content") || "").trim() || undefined,
  ));
}

function handleDebugSetPlayer(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(debugSetPlayerMessage(
    String(data.get("targetToken") || "").trim(),
    {
      mora: data.get("mora"),
      gold: data.get("gold"),
    },
  ));
}

function loadDemo() {
  disconnect(false);
  resetUiCollections(state);
  clearSettlement(state);
  resetMarketChartViewport();
  for (const message of buildSampleMessages(state.connection.role)) {
    applyMessage(state, message);
  }
  renderApp(state);
}

function showSummaryDetail(day) {
  const summary = state.dailySummaries.find((item) => String(item.day) === String(day));
  if (!summary) return;
  const body = document.getElementById("detailModalBody");
  const title = document.getElementById("detailModalTitle");
  const eyebrow = document.getElementById("detailModalEyebrow");
  if (!body || !title || !eyebrow) return;

  eyebrow.textContent = "每日总结";
  title.textContent = `第 ${summary.day} 日总结`;
  body.innerHTML = `
    <section class="detail-section">
      <h3>结算结果</h3>
      <p>胜者：${escapeHtml(summary.winnerToken || "Tie")}</p>
      <p>原因：${escapeHtml(summary.reason || "-")}</p>
    </section>
    <div class="detail-grid">
      ${(summary.players || []).map((player) => `
        <section class="detail-section">
          <h3>${escapeHtml(player.token)}</h3>
          <p>NAV：${escapeHtml(player.nav)}</p>
          <p>Mora：${escapeHtml(player.mora)}</p>
          <p>Gold：${escapeHtml(player.gold)}</p>
          <p>Trades：${escapeHtml(player.tradeCount)}</p>
          <p>Frozen Mora：${escapeHtml(player.frozenMora)}</p>
          <p>Frozen Gold：${escapeHtml(player.frozenGold)}</p>
          <p>Locked Gold：${escapeHtml(player.lockedGold)}</p>
        </section>
      `).join("")}
    </div>
  `;
  openModal("detailModal");
}

function showPlayerDetail(token) {
  const player = state.playerSummaries[token];
  if (!player) return;
  const body = document.getElementById("detailModalBody");
  const title = document.getElementById("detailModalTitle");
  const eyebrow = document.getElementById("detailModalEyebrow");
  if (!body || !title || !eyebrow) return;

  eyebrow.textContent = "操盘手";
  title.textContent = `${token} 摘要`;
  body.innerHTML = `
    <section class="detail-section">
      <h3>当前状态</h3>
      <p>NAV：${escapeHtml(player.nav)}</p>
      <p>Mora：${escapeHtml(player.mora)}</p>
      <p>Gold：${escapeHtml(player.gold)}</p>
      <p>Pending Orders：${escapeHtml(player.pendingOrderCount ?? 0)}</p>
      <p>Active Cards：${escapeHtml((player.activeCards || []).join("、") || "暂无")}</p>
    </section>
  `;
  openModal("detailModal");
}

function openModal(id) {
  document.getElementById(id)?.removeAttribute("hidden");
}

function closeModal(id) {
  document.getElementById(id)?.setAttribute("hidden", "");
}

function updateRoute() {
  const url = new URL(window.location.href);
  url.searchParams.set("mode", state.connection.role);
  url.searchParams.set("token", state.connection.token);
  url.searchParams.set("server", state.connection.server);
  window.history.replaceState({}, "", url);
}

function actionDetail(message) {
  if (message.messageType === "LIMIT_BUY" || message.messageType === "LIMIT_SELL") {
    return `price=${message.price} qty=${message.quantity}`;
  }
  if (message.messageType === "CANCEL_ORDER") {
    return `orderId=${message.orderId}`;
  }
  if (message.messageType === "SUBMIT_REPORT") {
    return `news=${message.newsId} ${message.prediction}`;
  }
  if (message.messageType === "SELECT_STRATEGY") {
    return String(message.cardName || "");
  }
  if (message.messageType === "ACTIVATE_SKILL") {
    return `${message.skillName || ""} ${message.direction || ""}`.trim();
  }
  return message.messageType;
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function initParticles() {
  const canvas = document.getElementById("particles-bg");
  if (!canvas) return;
  const ctx = canvas.getContext("2d");
  if (!ctx) return;

  function resize() {
    canvas.width = window.innerWidth;
    canvas.height = window.innerHeight;
  }
  resize();
  window.addEventListener("resize", resize);

  const COUNT = 50;
  const particles = Array.from({ length: COUNT }, () => ({
    x: Math.random() * canvas.width,
    y: Math.random() * canvas.height,
    r: 0.8 + Math.random() * 2,
    vx: (Math.random() - 0.5) * 0.3,
    vy: (Math.random() - 0.5) * 0.3,
    alpha: 0.1 + Math.random() * 0.3,
  }));

  function draw() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    for (const p of particles) {
      p.x += p.vx;
      p.y += p.vy;
      if (p.x < 0) p.x = canvas.width;
      if (p.x > canvas.width) p.x = 0;
      if (p.y < 0) p.y = canvas.height;
      if (p.y > canvas.height) p.y = 0;
      ctx.beginPath();
      ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2);
      ctx.fillStyle = `rgba(200, 168, 75, ${p.alpha})`;
      ctx.fill();
    }
    requestAnimationFrame(draw);
  }
  requestAnimationFrame(draw);
}
