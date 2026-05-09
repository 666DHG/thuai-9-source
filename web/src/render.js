import { applyColorScheme, COLOR_SCHEMES, readAppliedPalette } from "./appearance.js";
import { unreadNewsCount } from "./store.js";

const PLACEHOLDER = '<p class="placeholder">暂无数据</p>';
const MARKET_CHART_MIN_ZOOM = 0.12;
const MARKET_CHART_MAX_ZOOM = 32;
const MARKET_CHART_ZOOM_SENSITIVITY = 0.0015;
const MARKET_CHART_PAN_SENSITIVITY = 1;

const _prevPrices = {};
const _prevBookPrices = {};
const _seenEventIds = new Set();
const _seenTickerTradeIds = new Set();
let _lastTickerSignature = "";
const marketChartViewport = {
  zoom: 1,
  offset: 0,
  dragging: false,
  dragStartX: 0,
  dragStartOffset: 0,
};
const VIEW_TITLES = {
  main: "交易大厅",
  logs: "市场动态",
  rankings: "富豪榜",
  info: "我的账户",
  debug: "操盘台",
  "server-debug": "服务器控制台",
};

export function renderApp(state) {
  // Admin reuses the observer chrome so the server-debug panel shows up;
  // role-aware logic still reads state.connection.role directly.
  document.body.dataset.mode = state.connection.role === "admin" ? "observer" : state.connection.role;
  applyColorScheme(state.ui.colorScheme);

  if (state.connection.role !== "player" && (state.ui.activeView === "info" || state.ui.activeView === "debug")) {
    state.ui.activeView = "main";
  }
  if (state.connection.role === "player" && state.ui.activeView === "server-debug") {
    state.ui.activeView = "main";
  }

  setText("stageValue", state.game.stage || "-");
  setText("dayValue", state.game.currentDay || "-");
  setText("tickValue", displayTick(state));
  setText("viewTitle", VIEW_TITLES[state.ui.activeView] || "交易大厅");
  renderConnection(state);
  renderControls(state);
  renderViewSwitch(state);
  renderScoreboard(state);
  renderPrices(state);
  renderOrderBook(state);
  renderNews(state);
  renderEvents(state);
  renderDailySummaries(state);
  renderPlayerComparison(state);
  renderPortfolio(state);
  renderOrders(state);
  renderStrategy(state);
  renderSettlement(state);
  renderTicker(state);
  drawMarketChart(document.getElementById("marketCanvas"), state);
  keepFeedsPinned();
}

export function resetMarketChartViewport() {
  marketChartViewport.zoom = 1;
  marketChartViewport.offset = 0;
  marketChartViewport.dragging = false;
  marketChartViewport.dragStartX = 0;
  marketChartViewport.dragStartOffset = 0;
}

export function handleMarketChartPointerDown(canvas, event, state) {
  if (!canvas || event.button !== 0) return false;
  marketChartViewport.dragging = true;
  marketChartViewport.dragStartX = event.clientX;
  marketChartViewport.dragStartOffset = marketChartViewport.offset;
  canvas.classList.add("is-dragging");
  if (typeof canvas.setPointerCapture === "function") {
    try {
      canvas.setPointerCapture(event.pointerId);
    } catch {
      // Ignore best-effort pointer capture failures.
    }
  }
  event.preventDefault();
  return true;
}

export function handleMarketChartPointerMove(canvas, event, state) {
  if (!canvas || !marketChartViewport.dragging) return false;
  const layout = getMarketChartLayout(canvas, state);
  if (!layout) return false;
  const dx = event.clientX - marketChartViewport.dragStartX;
  marketChartViewport.offset = clamp(
    marketChartViewport.dragStartOffset - (dx / layout.step) * MARKET_CHART_PAN_SENSITIVITY,
    0,
    layout.maxOffset,
  );
  event.preventDefault();
  return true;
}

export function handleMarketChartPointerUp(canvas, event) {
  if (!marketChartViewport.dragging) return false;
  marketChartViewport.dragging = false;
  if (canvas) {
    canvas.classList.remove("is-dragging");
    if (typeof canvas.releasePointerCapture === "function" && event?.pointerId !== undefined) {
      try {
        canvas.releasePointerCapture(event.pointerId);
      } catch {
        // Ignore release failures.
      }
    }
  }
  return true;
}

export function handleMarketChartWheel(canvas, event, state) {
  if (!canvas) return false;
  const layout = getMarketChartLayout(canvas, state);
  if (!layout) return false;

  if (Math.abs(event.deltaX) > Math.abs(event.deltaY) || event.shiftKey) {
    const delta = event.shiftKey && Math.abs(event.deltaX) <= Math.abs(event.deltaY)
      ? event.deltaY
      : event.deltaX;
    if (!Number.isFinite(delta) || delta === 0) return false;
    marketChartViewport.offset = clamp(
      marketChartViewport.offset + (delta / layout.step) * MARKET_CHART_PAN_SENSITIVITY,
      0,
      layout.maxOffset,
    );
    event.preventDefault();
    return true;
  }

  const zoomPoint = event.clientX - layout.rect.left - layout.margin.left;
  const anchorIndex = marketChartViewport.offset + zoomPoint / layout.step;
  const nextZoom = clamp(
    marketChartViewport.zoom * Math.exp(-clamp(event.deltaY, -240, 240) * MARKET_CHART_ZOOM_SENSITIVITY),
    MARKET_CHART_MIN_ZOOM,
    MARKET_CHART_MAX_ZOOM,
  );
  const nextStep = layout.baseStep * nextZoom;
  marketChartViewport.zoom = nextZoom;
  marketChartViewport.offset = clamp(anchorIndex - zoomPoint / nextStep, 0, layout.maxOffsetForStep(nextStep));
  event.preventDefault();
  return true;
}

function renderConnection(state) {
  const badge = document.getElementById("connectionBadge");
  if (!badge) return;
  const labels = {
    idle: "未连接",
    connecting: "连接中",
    connected: "已连接",
    disconnected: "已断开",
    error: "连接错误",
  };
  badge.textContent = labels[state.connection.status] || state.connection.status;
  badge.dataset.status = state.connection.status;
}

function renderControls(state) {
  const portInput = document.getElementById("portInput");
  const tokenInput = document.getElementById("tokenInput");
  const priceModeSelect = document.getElementById("priceModeSelect");
  const intervalSelect = document.getElementById("intervalSelect");
  const colorSchemeSelect = document.getElementById("colorSchemeSelect");

  if (portInput && document.activeElement !== portInput) {
    const match = state.connection.server.match(/:(\d+)$/);
    portInput.value = match ? match[1] : "14514";
  }
  setInputValue(tokenInput, state.connection.token);
  setInputValue(priceModeSelect, state.market.priceField);
  setInputValue(intervalSelect, String(state.market.interval));
  fillColorSchemeOptions(colorSchemeSelect);
  setInputValue(colorSchemeSelect, state.ui.colorScheme);

  document.querySelectorAll("[data-mode]").forEach((button) => {
    button.classList.toggle("is-active", button.dataset.mode === state.connection.role);
  });
}

function renderViewSwitch(state) {
  document.querySelectorAll("[data-view]").forEach((button) => {
    button.classList.toggle("is-active", button.dataset.view === state.ui.activeView);
  });
  document.querySelectorAll("[data-view-panel]").forEach((panel) => {
    panel.classList.toggle("is-active", panel.dataset.viewPanel === state.ui.activeView);
  });
}

function renderScoreboard(state) {
  const node = document.getElementById("scoreboard");
  if (!node) return;
  if (!state.game.scores.length) {
    node.innerHTML = PLACEHOLDER;
    return;
  }

  node.innerHTML = state.game.scores
    .map((score) => `
      <div class="score-row">
        <span>${escapeHtml(score.token)}</span>
        <strong>${escapeHtml(score.score)}</strong>
      </div>
    `)
    .join("");
}

function renderPrices(state) {
  const bids = state.market.bids;
  const asks = state.market.asks;
  const bestBid = bids[0]?.price ?? 0;
  const bestAsk = asks[0]?.price ?? 0;
  const lastPrice = state.market.lastPrice;

  flashIfChanged("bestBidValue", bestBid, formatNumber(bestBid));
  flashIfChanged("bestAskValue", bestAsk, formatNumber(bestAsk));
  setText("spreadValue", bestBid && bestAsk ? formatNumber(bestAsk - bestBid) : "-");
  flashIfChanged("midValue", state.market.midPrice, formatNumber(state.market.midPrice));
  flashIfChanged("lastValue", lastPrice, formatNumber(lastPrice));
  setText("volumeValue", formatNumber(state.market.volume));
}

function flashIfChanged(id, newVal, displayText) {
  const node = document.getElementById(id);
  if (!node) return;
  const prev = _prevPrices[id];
  node.textContent = displayText;
  if (prev !== undefined && prev !== newVal && newVal !== 0) {
    node.classList.remove("price-up", "price-down");
    void node.offsetWidth;
    node.classList.add(newVal > prev ? "price-up" : "price-down");
    setTimeout(() => node.classList.remove("price-up", "price-down"), 400);
  }
  _prevPrices[id] = newVal;
}

function renderOrderBook(state) {
  const bids = state.market.bids;
  const asks = state.market.asks;

  renderBookList("bidsList", bids, "bid");
  renderBookList("asksList", asks, "ask");
}

function renderNews(state) {
  const feedNode = document.getElementById("newsFeed");
  const unreadNode = document.getElementById("newsUnreadBadge");
  const quickReportForm = document.getElementById("quickReportForm");
  if (!feedNode || !unreadNode) return;

  const newsItems = state.news.items || [];
  const unread = unreadNewsCount(state);
  unreadNode.textContent = String(unread);
  unreadNode.hidden = unread <= 0;

  if (!newsItems.length) {
    feedNode.innerHTML = PLACEHOLDER;
    if (quickReportForm) {
      quickReportForm.hidden = true;
      quickReportForm.newsId.value = "";
    }
    return;
  }

  feedNode.innerHTML = newsItems
    .map((item, index) => renderNewsCard(item, state.news.results[item.newsId], index === 0))
    .join("");

  if (quickReportForm) {
    quickReportForm.hidden = state.connection.role !== "player";
    quickReportForm.newsId.value = newsItems[0].newsId;
  }
}

function renderNewsCard(news, result, isLatest) {
  const fakeBadge = news.isFake
    ? '<span class="news-badge danger">伪造</span>'
    : '<span class="news-badge">公开</span>';
  const source = news.sourcePlayer ? `<span>来源 ${escapeHtml(news.sourcePlayer)}</span>` : "";
  const resultMarkup = result
    ? `
      <div class="report-result ${result.isCorrect ? "correct" : "wrong"}">
        <span>${escapeHtml(result.prediction || "-")}</span>
        <strong>${result.isCorrect ? "命中" : "偏离"}</strong>
        <span>奖惩 ${formatNumber(result.reward)}</span>
      </div>
    `
    : "";

  return `
    <article class="news-card ${isLatest ? "is-latest" : ""}">
      <div class="news-meta">
        <span>#${escapeHtml(news.newsId || "-")}</span>
        <span>D${escapeHtml(news.day || "-")} T${escapeHtml(news.publishTick || "-")}</span>
        ${fakeBadge}
        ${source}
      </div>
      <p>${escapeHtml(news.content || "暂无正文")}</p>
      ${resultMarkup}
    </article>
  `;
}

function renderBookList(id, levels, side) {
  const node = document.getElementById(id);
  if (!node) return;
  if (!levels.length) {
    node.innerHTML = PLACEHOLDER;
    _prevBookPrices[id] = [];
    return;
  }

  const previous = _prevBookPrices[id] || [];
  const next = [];
  node.innerHTML = levels
    .map((level, index) => {
      const price = Number(level.price);
      next[index] = price;
      const prev = previous[index];
      const flashClass = prev !== undefined && prev !== price
        ? ` ${price > prev ? "price-up" : "price-down"}`
        : "";
      return `
        <div class="book-row ${side}">
          <span class="${flashClass.trim()}">${formatNumber(level.price)}</span>
          <span>${formatNumber(level.quantity)}</span>
        </div>
      `;
    })
    .join("");
  _prevBookPrices[id] = next;
}

function renderEvents(state) {
  const modalNode = document.getElementById("eventFeed");
  const previewNode = document.getElementById("eventPreview");
  const currentIds = new Set(state.events.map((event) => event.id));
  _seenEventIds.forEach((id) => {
    if (!currentIds.has(id)) _seenEventIds.delete(id);
  });
  const newEventIds = new Set(state.events.filter((event) => !_seenEventIds.has(event.id)).map((event) => event.id));
  const items = state.events.length
    ? state.events.map((event) => eventMarkup(event, newEventIds.has(event.id))).join("")
    : PLACEHOLDER;

  if (modalNode) {
    modalNode.innerHTML = items;
  }
  if (previewNode) {
    previewNode.innerHTML = state.events.length
      ? state.events.slice(0, 12).map((event) => eventMarkup(event, newEventIds.has(event.id))).join("")
      : PLACEHOLDER;
  }
  newEventIds.forEach((id) => _seenEventIds.add(id));
}

function eventMarkup(event, isNew) {
  const isImportant = event.kind === "trade" || event.kind === "news" || event.kind === "settlement";
  const enterClass = isNew ? " event-enter" : "";
  const importantClass = isNew && isImportant ? " event-important" : "";
  return `
    <article class="event-item${enterClass}${importantClass}" data-kind="${escapeHtml(event.kind)}">
      <strong>${escapeHtml(event.title)}</strong>
      <p>D${escapeHtml(event.day || "-")} T${escapeHtml(event.tick || "-")} ${escapeHtml(event.detail)}</p>
    </article>
  `;
}

function renderDailySummaries(state) {
  const node = document.getElementById("dailySummary");
  if (!node) return;
  if (!state.dailySummaries.length) {
    node.innerHTML = PLACEHOLDER;
    return;
  }

  node.innerHTML = state.dailySummaries
    .map((summary) => `
      <article class="day-summary">
        <div class="day-summary-head">
          <strong>Day ${escapeHtml(summary.day)}</strong>
          <span>Winner: ${escapeHtml(summary.winnerToken || "Tie")}</span>
          <button type="button" class="summary-link ghost-button" data-action="open-summary" data-summary-day="${escapeAttribute(summary.day)}">查看完整总结</button>
        </div>
      </article>
    `)
    .join("");
}

function renderPlayerComparison(state) {
  const node = document.getElementById("playerComparison");
  if (!node) return;
  const summaries = Object.values(state.playerSummaries);
  if (!summaries.length) {
    node.innerHTML = PLACEHOLDER;
    return;
  }

  node.innerHTML = summaries
    .map((player) => `
      <button type="button" class="comparison-card comparison-link" data-player-token="${escapeAttribute(player.token)}">
        <h3>${escapeHtml(player.token)}</h3>
      </button>
    `)
    .join("");
}

function renderPortfolio(state) {
  const node = document.getElementById("portfolioGrid");
  if (!node) return;
  const player = state.player;
  node.innerHTML = [
    statCell("NAV", player.nav),
    statCell("Mora", player.mora),
    statCell("Frozen Mora", player.frozenMora),
    statCell("Gold", player.gold),
    statCell("Frozen Gold", player.frozenGold),
    statCell("Locked Gold", player.lockedGold),
  ].join("");
}

function renderOrders(state) {
  const node = document.getElementById("ordersTable");
  if (!node) return;
  const orders = state.player.pendingOrders || [];
  if (!orders.length) {
    node.innerHTML = '<tr><td colspan="6" class="placeholder">暂无挂单</td></tr>';
    return;
  }

  node.innerHTML = orders
    .map((order) => `
      <tr>
        <td>${escapeHtml(order.orderId)}</td>
        <td><span class="side-pill ${sideClass(order.side)}">${escapeHtml(order.side)}</span></td>
        <td>${formatNumber(order.price)}</td>
        <td>${formatNumber(order.quantity)}</td>
        <td>${formatNumber(order.remainingQuantity)}</td>
        <td>${escapeHtml(order.status)}</td>
      </tr>
    `)
    .join("");
}

function renderStrategy(state) {
  const optionsNode = document.getElementById("strategyOptions");
  const activeNode = document.getElementById("activeCards");
  if (!optionsNode || !activeNode) return;

  const options = state.strategy.options;
  const cards = options
    ? [
        ["基建", options.infrastructure],
        ["风控", options.riskControl],
        ["金融科技", options.finTech],
      ].filter(([, card]) => card)
    : [];

  optionsNode.innerHTML = cards.length
    ? cards.map(([label, card]) => renderStrategyCard(label, card, state.connection.role)).join("")
    : PLACEHOLDER;

  activeNode.innerHTML = renderTags(state.player.activeCards || []);
}

function renderStrategyCard(label, card, role) {
  const action = role === "player"
    ? `<button type="button" data-action="select-strategy" data-card-name="${escapeAttribute(card.name)}">选择</button>`
    : "";

  return `
    <article class="strategy-card">
      <strong>${escapeHtml(label)} · ${escapeHtml(card.name)}</strong>
      <p>${escapeHtml(card.description || card.category || "")}</p>
      ${action}
    </article>
  `;
}

function renderSettlement(state) {
  const modal = document.getElementById("settlementModal");
  const title = document.getElementById("settlementTitle");
  const body = document.getElementById("settlementBody");
  if (!modal || !title || !body) return;

  const wasHidden = modal.hidden;
  modal.hidden = !state.ui.showSettlement || !state.settlement;
  if (!state.settlement) return;

  title.textContent = `第 ${state.settlement.day ?? state.game.currentDay} 日结算`;
  const rows = Array.isArray(state.settlement.players) ? state.settlement.players : [];

  body.innerHTML = `
    <div class="settlement-row">
      <span>胜者</span>
      <strong>${escapeHtml(state.settlement.winnerToken || "-")}</strong>
      <span>${escapeHtml(state.settlement.reason || "-")}</span>
    </div>
    ${rows
      .map((player) => `
        <div class="settlement-row">
          <span>${escapeHtml(player.token)}</span>
          <strong>NAV ${formatNumber(player.nav)}</strong>
          <span>${formatNumber(player.tradeCount)} 笔成交</span>
        </div>
      `)
      .join("")}
  `;

  if (wasHidden && !modal.hidden) {
    const inner = modal.querySelector(".settlement-modal-inner");
    if (inner) {
      inner.style.animation = "none";
      void inner.offsetWidth;
      inner.style.animation = "";
    }
    animateSettlementNumbers(body);
  }
}

function animateSettlementNumbers(container) {
  container.querySelectorAll("strong").forEach((el) => {
    const text = el.textContent;
    const match = text.match(/[\d,]+(\.\d+)?/);
    if (!match) return;
    const target = parseFloat(match[0].replace(/,/g, ""));
    if (!Number.isFinite(target) || target === 0) return;
    const prefix = text.slice(0, text.indexOf(match[0]));
    const suffix = text.slice(text.indexOf(match[0]) + match[0].length);
    const start = performance.now();
    const duration = 700;
    function tick(now) {
      const t = Math.min((now - start) / duration, 1);
      const eased = 1 - Math.pow(1 - t, 3);
      const current = Math.round(target * eased);
      el.textContent = prefix + new Intl.NumberFormat("en-US").format(current) + suffix;
      if (t < 1) requestAnimationFrame(tick);
    }
    requestAnimationFrame(tick);
  });
}

function keepFeedsPinned() {
  document.querySelectorAll(".auto-scroll").forEach((node) => {
    node.scrollTop = 0;
  });
}

function renderTicker(state) {
  const track = document.getElementById("tickerTrack");
  const timeEl = document.getElementById("tickerTime");
  if (!track) return;

  const trades = state.events.filter((e) => e.kind === "trade").slice(0, 40);

  if (timeEl) {
    const day = state.game.currentDay ?? "-";
    const tick = state.market.tick ?? state.game.dayTick ?? state.game.totalTicks ?? state.game.currentTick ?? "-";
    timeEl.textContent = day !== "-" || tick !== "-" ? `第${day}日 Tick ${tick}` : "—";
  }

  if (!trades.length) {
    track.innerHTML = '<span class="ticker-item">等待行情数据...</span>';
    _lastTickerSignature = "";
    _seenTickerTradeIds.clear();
    return;
  }

  const signature = trades.map((event) => event.id).join("|");
  if (signature === _lastTickerSignature) return;
  _lastTickerSignature = signature;
  const newTradeIds = new Set(trades.filter((event) => !_seenTickerTradeIds.has(event.id)).map((event) => event.id));

  const items = trades.map((e, i) => {
    const detail = String(e.detail || "");
    const priceMatch = detail.match(/price=([\d.]+)/);
    const qtyMatch = detail.match(/qty=([\d.]+)/);
    const sideMatch = detail.match(/side=(\w+)/);
    const price = priceMatch ? Number(priceMatch[1]) : 0;
    const qty = qtyMatch ? Number(qtyMatch[1]) : 0;
    const side = sideMatch ? sideMatch[1].toLowerCase() : "";
    const cls = side === "buy" ? "up" : side === "sell" ? "down" : "";
    const arrow = cls === "up" ? "▲" : cls === "down" ? "▼" : "·";
    const sideLabel = side === "buy" ? "买入" : side === "sell" ? "卖出" : "成交";
    const flashClass = newTradeIds.has(e.id) || i === 0 ? " flash-new" : "";
    return `<span class="ticker-item ${cls}${flashClass}">¥${formatNumber(price)} ${arrow} ${sideLabel} ${formatNumber(qty)}手</span>`;
  }).join("");

  track.innerHTML = items;
  newTradeIds.forEach((id) => _seenTickerTradeIds.add(id));
}

export function drawMarketChart(canvas, state) {
  if (!canvas) return;
  const palette = readAppliedPalette();
  const rect = canvas.getBoundingClientRect();
  const dpr = window.devicePixelRatio || 1;
  const width = Math.max(320, Math.floor(rect.width));
  const height = Math.max(260, Math.floor(rect.height));

  if (canvas.width !== width * dpr || canvas.height !== height * dpr) {
    canvas.width = width * dpr;
    canvas.height = height * dpr;
  }

  const ctx = canvas.getContext("2d");
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, width, height);
  ctx.fillStyle = "#0d1117";
  ctx.fillRect(0, 0, width, height);

  const candles = state.market.candles;
  if (!candles.length) {
    ctx.fillStyle = "#8b949e";
    ctx.font = "14px 'JetBrains Mono', monospace";
    ctx.fillText("等待 MARKET_STATE", 24, 40);
    return;
  }

  const margin = { top: 18, right: 58, bottom: 42, left: 44 };
  const chartHeight = height - margin.top - margin.bottom;
  const volumeHeight = Math.max(54, chartHeight * 0.22);
  const priceHeight = chartHeight - volumeHeight - 12;
  const plotWidth = width - margin.left - margin.right;
  const baseStep = getBaseStep(state.market.interval);
  const step = baseStep * marketChartViewport.zoom;
  const visibleCount = plotWidth / step;
  const maxOffset = Math.max(0, candles.length - visibleCount);
  marketChartViewport.offset = clamp(marketChartViewport.offset, 0, maxOffset);
  const candleWidth = Math.max(2, Math.min(14, step * 0.58));
  const firstIndex = Math.max(0, Math.floor(marketChartViewport.offset) - 1);
  const lastIndex = Math.min(candles.length - 1, Math.ceil(marketChartViewport.offset + visibleCount) + 1);
  const visibleCandles = candles.slice(firstIndex, lastIndex + 1);

  const prices = visibleCandles.flatMap((candle) => [candle.high, candle.low]);
  const minPrice = Math.min(...prices);
  const maxPrice = Math.max(...prices);
  const pricePadding = Math.max(1, (maxPrice - minPrice) * 0.08);
  const priceMin = minPrice - pricePadding;
  const priceMax = maxPrice + pricePadding;
  const maxVolume = Math.max(1, ...visibleCandles.map((candle) => candle.volume));

  drawGrid(ctx, margin, width, height, priceHeight, priceMin, priceMax);

  for (let index = firstIndex; index <= lastIndex; index += 1) {
    const candle = candles[index];
    if (!candle) continue;
    const x = margin.left + (index - marketChartViewport.offset + 0.5) * step;
    if (x < margin.left - step || x > width - margin.right + step) continue;
    const openY = mapRange(candle.open, priceMin, priceMax, margin.top + priceHeight, margin.top);
    const closeY = mapRange(candle.close, priceMin, priceMax, margin.top + priceHeight, margin.top);
    const highY = mapRange(candle.high, priceMin, priceMax, margin.top + priceHeight, margin.top);
    const lowY = mapRange(candle.low, priceMin, priceMax, margin.top + priceHeight, margin.top);
    const up = candle.close >= candle.open;
    const color = up ? palette.up : palette.down;
    const bodyTop = Math.min(openY, closeY);
    const bodyHeight = Math.max(2, Math.abs(closeY - openY));

    ctx.shadowBlur = 6;
    ctx.shadowColor = color;
    ctx.strokeStyle = color;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(x, highY);
    ctx.lineTo(x, lowY);
    ctx.stroke();

    ctx.fillStyle = color;
    ctx.fillRect(x - candleWidth / 2, bodyTop, candleWidth, bodyHeight);
    ctx.shadowBlur = 0;

    const volumeTop = margin.top + priceHeight + 12;
    const barHeight = (candle.volume / maxVolume) * volumeHeight;
    ctx.shadowBlur = 3;
    ctx.shadowColor = color;
    ctx.globalAlpha = 0.35;
    ctx.fillRect(x - candleWidth / 2, volumeTop + volumeHeight - barHeight, candleWidth, barHeight);
    ctx.globalAlpha = 1;
    ctx.shadowBlur = 0;
  }

  const firstVisible = candles[Math.max(0, firstIndex)];
  const lastVisible = candles[Math.min(candles.length - 1, lastIndex)];
  ctx.fillStyle = "#8b949e";
  ctx.font = "11px 'JetBrains Mono', monospace";
  ctx.fillText(`${Math.round(priceMax)}`, width - margin.right + 10, margin.top + 6);
  ctx.fillText(`${Math.round(priceMin)}`, width - margin.right + 10, margin.top + priceHeight);
  ctx.fillText(`D${firstVisible.day} T${firstVisible.bucketStartTick} - T${lastVisible.bucketEndTick}`, margin.left, height - 14);
}

function drawGrid(ctx, margin, width, height, priceHeight, priceMin, priceMax) {
  ctx.strokeStyle = "#21262d";
  ctx.lineWidth = 1;
  ctx.beginPath();
  for (let i = 0; i <= 4; i += 1) {
    const y = margin.top + (priceHeight / 4) * i;
    ctx.moveTo(margin.left, y);
    ctx.lineTo(width - margin.right, y);
  }
  ctx.moveTo(margin.left, height - margin.bottom);
  ctx.lineTo(width - margin.right, height - margin.bottom);
  ctx.stroke();

  ctx.fillStyle = "#8b949e";
  ctx.font = "11px 'JetBrains Mono', monospace";
  ctx.fillText("OHLC", margin.left, margin.top - 4);
  ctx.fillText(`${Math.round((priceMin + priceMax) / 2)}`, width - margin.right + 10, margin.top + priceHeight / 2);
}

function getMarketChartLayout(canvas, state) {
  if (!canvas) return null;
  const rect = canvas.getBoundingClientRect();
  const width = Math.max(320, Math.floor(rect.width));
  const margin = { top: 18, right: 58, bottom: 42, left: 44 };
  const plotWidth = Math.max(1, width - margin.left - margin.right);
  const baseStep = getBaseStep(state.market.interval);
  const step = baseStep * marketChartViewport.zoom;
  const visibleCount = plotWidth / step;
  const maxOffset = Math.max(0, state.market.candles.length - visibleCount);
  return {
    rect,
    margin,
    step,
    baseStep,
    maxOffset,
    plotWidth,
    maxOffsetForStep(nextStep) {
      return Math.max(0, state.market.candles.length - plotWidth / nextStep);
    },
  };
}

function getBaseStep(interval) {
  const normalized = Number(interval) || 20;
  return Math.max(4, 8 + (normalized - 20) * 0.18);
}

function statCell(label, value) {
  return `<div><span>${escapeHtml(label)}</span><strong>${formatNumber(value)}</strong></div>`;
}

function fillColorSchemeOptions(select) {
  if (!select || select.options.length) return;
  select.innerHTML = COLOR_SCHEMES
    .map((scheme) => `<option value="${escapeAttribute(scheme.value)}">${escapeHtml(scheme.label)}</option>`)
    .join("");
}

function sideClass(value) {
  return String(value || "").toLowerCase() === "sell" ? "sell" : "buy";
}

function renderTags(tags) {
  if (!tags.length) {
    return '<span class="placeholder">暂无策略</span>';
  }
  return tags.map((tag) => `<span class="tag">${escapeHtml(tag)}</span>`).join("");
}

function displayTick(state) {
  const dayTick = state.market.tick ?? state.game.dayTick ?? state.game.totalTicks;
  const globalTick = state.game.currentTick;
  if (dayTick !== undefined && globalTick !== undefined && dayTick !== null && globalTick !== null) {
    return `${dayTick} / ${globalTick}`;
  }
  return dayTick ?? globalTick ?? "-";
}

function mapRange(value, inMin, inMax, outMin, outMax) {
  if (inMax === inMin) return (outMin + outMax) / 2;
  return outMin + ((value - inMin) / (inMax - inMin)) * (outMax - outMin);
}

function setText(id, value) {
  const node = document.getElementById(id);
  if (node) node.textContent = value;
}

function setInputValue(node, value) {
  if (node && document.activeElement !== node) {
    node.value = value ?? "";
  }
}

function formatNumber(value) {
  const number = Number(value);
  if (!Number.isFinite(number) || number === 0) return number === 0 ? "0" : "-";
  return new Intl.NumberFormat("en-US").format(number);
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function escapeAttribute(value) {
  return escapeHtml(value).replaceAll("`", "&#096;");
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}
