import {
  DEFAULT_CANDLE_INTERVAL,
  DEFAULT_PRICE_FIELD,
  ingestMarketSnapshot,
  createCandleAccumulator,
  normalizeInterval,
} from "./candles.js";
import { DEFAULT_COLOR_SCHEME, normalizeColorScheme } from "./appearance.js";
import {
  DEFAULT_LOCALHOST_PORT,
  DEFAULT_SERVER_URL,
  normalizeServerUrl,
  parseServerChoice,
} from "./connection.js";

const MAX_EVENTS = 160;
const MAX_MARKET_HISTORY = 8000;
const MAX_NEWS = 80;
const VALID_VIEWS = new Set(["main", "logs", "rankings", "info", "debug", "server-debug"]);

export function routeFromLocation(location) {
  const search = new URLSearchParams(location.search);
  const pathMode = location.pathname.includes("player") ? "player" : "observer";
  const mode = search.get("mode") || pathMode;
  const role = mode === "player" ? "player" : mode === "admin" ? "admin" : "observer";
  const rawServer = search.get("server") || DEFAULT_SERVER_URL;
  const server = normalizeServerUrl(rawServer);
  const serverChoice = parseServerChoice(server);
  return {
    role,
    token: search.get("token") || "player1",
    adminSecret: search.get("secret") || "",
    server,
    localhostPort: serverChoice.localhostPort,
  };
}

export function createInitialState(route = {}) {
  return {
    connection: {
      role: route.role || "observer",
      token: route.token || "player1",
      adminSecret: route.adminSecret || "",
      server: route.server || DEFAULT_SERVER_URL,
      localhostPort: route.localhostPort || DEFAULT_LOCALHOST_PORT,
      status: "idle",
      protocolVersion: "legacy",
      capabilities: [],
      reconnectAttempt: 0,
      lastError: "",
      lastSent: "",
    },
    game: {
      stage: "",
      currentDay: 0,
      currentTick: 0,
      totalTicks: 0,
      stageTick: 0,
      stageTickLimit: 0,
      dayTick: 0,
      dayTickLimit: 2000,
      scores: [],
    },
    market: {
      bids: [],
      asks: [],
      lastPrice: 0,
      midPrice: 0,
      volume: 0,
      tick: 0,
      interval: DEFAULT_CANDLE_INTERVAL,
      priceField: DEFAULT_PRICE_FIELD,
      history: [],
      candles: [],
      candleAccumulator: createCandleAccumulator(),
    },
    player: emptyPlayerState(),
    playerSummaries: {},
    dailySummaries: [],
    strategy: {
      options: null,
    },
    news: {
      items: [],
      results: {},
    },
    events: [],
    settlement: null,
    ui: {
      eventCounter: 0,
      showSettlement: false,
      colorScheme: normalizeColorScheme(route.colorScheme || DEFAULT_COLOR_SCHEME),
      activeView: "main",
      readNewsCount: 0,
    },
  };
}

export function setConnectionPatch(state, patch) {
  Object.assign(state.connection, patch);
}

export function setMode(state, role) {
  state.connection.role = role === "player" ? "player" : role === "admin" ? "admin" : "observer";
  if (state.connection.role !== "player" && (state.ui.activeView === "info" || state.ui.activeView === "debug")) {
    state.ui.activeView = "main";
  }
  if (state.connection.role !== "admin" && state.ui.activeView === "server-debug") {
    state.ui.activeView = "main";
  }
}

export function setActiveView(state, view) {
  const nextView = String(view || "main");
  state.ui.activeView = VALID_VIEWS.has(nextView) ? nextView : "main";
}

export function markNewsAsRead(state) {
  state.ui.readNewsCount = state.news.items.length;
}

export function resetUiCollections(state) {
  state.events = [];
  state.news.items = [];
  state.news.results = {};
  state.dailySummaries = [];
  state.playerSummaries = {};
  state.ui.readNewsCount = 0;
}

export function unreadNewsCount(state) {
  return Math.max(0, state.news.items.length - state.ui.readNewsCount);
}

export function setCandleOptions(state, options) {
  state.market.interval = normalizeInterval(options.interval ?? state.market.interval);
  state.market.priceField = options.priceField || state.market.priceField;
  const accumulator = createCandleAccumulator({
    interval: state.market.interval,
    priceField: state.market.priceField,
  });
  for (const snapshot of state.market.history) {
    ingestMarketSnapshot(accumulator, snapshot);
  }
  state.market.candleAccumulator = accumulator;
  state.market.candles = accumulator.candles;
}

export function setColorScheme(state, value) {
  state.ui.colorScheme = normalizeColorScheme(value);
}

export function applyMessage(state, message) {
  if (!message || typeof message !== "object") return;

  switch (message.messageType) {
    case "HELLO_ACK":
      state.connection.protocolVersion = message.protocolVersion || "v1";
      state.connection.capabilities = Array.isArray(message.capabilities)
        ? message.capabilities
        : [];
      state.connection.role = message.role || state.connection.role;
      state.connection.token = message.token || state.connection.token;
      pushEvent(state, {
        kind: "system",
        title: "握手完成",
        detail: `protocol=${state.connection.protocolVersion}`,
      });
      break;

    case "GAME_STATE":
      state.game = {
        ...state.game,
        stage: message.stage || "",
        currentDay: numberOr(message.currentDay, 0),
        currentTick: numberOr(message.currentTick, 0),
        totalTicks: numberOr(message.totalTicks, 0),
        stageTick: numberOr(message.stageTick, state.game.stageTick),
        stageTickLimit: numberOr(message.stageTickLimit, state.game.stageTickLimit),
        dayTick: numberOr(message.dayTick, state.market.tick || state.game.dayTick),
        dayTickLimit: numberOr(message.dayTickLimit, state.game.dayTickLimit),
        scores: Array.isArray(message.scores) ? message.scores : [],
      };
      break;

    case "MARKET_STATE":
      state.market.bids = Array.isArray(message.bids) ? message.bids : [];
      state.market.asks = Array.isArray(message.asks) ? message.asks : [];
      state.market.lastPrice = numberOr(message.lastPrice, 0);
      state.market.midPrice = numberOr(message.midPrice, 0);
      state.market.volume = numberOr(message.volume, 0);
      state.market.tick = numberOr(message.tick, 0);
      state.game.dayTick = state.market.tick;
      appendMarketSnapshot(state);
      break;

    case "PLAYER_STATE":
      state.player = normalizePlayerState(message);
      break;

    case "PLAYER_SUMMARY_STATE":
      if (message.playerId !== undefined) {
        state.playerSummaries[message.playerId] = { ...message };
      }
      break;

    case "NEWS_BROADCAST":
      upsertNews(state, message);
      pushEvent(state, {
        kind: "news",
        title: `新闻 #${message.newsId ?? "-"}`,
        detail: message.content || "",
        tick: numberOr(message.publishTick, state.market.tick),
      });
      break;

    case "REPORT_RESULT":
      upsertReportResult(state, message);
      pushEvent(state, {
        kind: "report",
        title: `研报 ${message.isCorrect ? "正确" : "错误"}`,
        detail: `news=${message.newsId ?? "-"} prediction=${message.prediction ?? "-"} reward=${message.reward ?? 0} change=${message.actualChange ?? 0}`,
      });
      break;

    case "STRATEGY_OPTIONS":
      state.strategy.options = {
        infrastructure: message.infrastructure || null,
        riskControl: message.riskControl || null,
        finTech: message.finTech || null,
      };
      break;

    case "TRADE_NOTIFICATION":
      pushEvent(state, {
        kind: "trade",
        title: `成交 #${message.tradeId ?? "-"}`,
        detail: `price=${message.price ?? 0} qty=${message.quantity ?? 0} side=${message.side ?? "-"}`,
        tick: numberOr(message.tick, state.market.tick),
      });
      break;

    case "SKILL_EFFECT":
      pushEvent(state, {
        kind: "skill",
        title: message.skillName || "技能触发",
        detail: `${playerDisplayName(state, message.sourcePlayerId)} ${message.description || ""}`.trim(),
      });
      break;

    case "DAY_SETTLEMENT":
      state.settlement = { ...message };
      upsertDailySummary(state, message);
      state.ui.showSettlement = true;
      pushEvent(state, {
        kind: "settlement",
        title: `第 ${message.day ?? state.game.currentDay} 日结算`,
        detail: `winner=${message.winnerPlayerId >= 0 ? playerDisplayName(state, message.winnerPlayerId) : "Tie"} reason=${message.reason || "-"}`,
      });
      break;

    case "ERROR":
      state.connection.lastError = message.message || "";
      pushEvent(state, {
        kind: "error",
        title: `错误 ${message.errorCode ?? ""}`.trim(),
        detail: message.message || "",
      });
      break;

    case "DEBUG_ACK":
      pushEvent(state, {
        kind: message.ok ? "system" : "error",
        title: `${message.command || "DEBUG"} · ${message.ok ? "ok" : "fail"}`,
        detail: message.error || "",
      });
      break;

    case "DEBUG_QUERY_RESPONSE":
      pushEvent(state, {
        kind: "system",
        title: `DEBUG_QUERY · ${message.stage || "?"}`,
        detail: JSON.stringify(message, null, 2),
      });
      break;

    default:
      pushEvent(state, {
        kind: "system",
        title: message.messageType || "未知消息",
        detail: "未识别的 messageType",
      });
      break;
  }
}

function upsertDailySummary(state, message) {
  const day = numberOr(message.day, state.game.currentDay);
  const summary = {
    day,
    winnerPlayerId: numberOr(message.winnerPlayerId, -1),
    reason: message.reason || "",
    players: Array.isArray(message.players) ? message.players : [],
  };
  const index = state.dailySummaries.findIndex((item) => item.day === day);
  if (index >= 0) {
    state.dailySummaries[index] = summary;
  } else {
    state.dailySummaries.push(summary);
  }
  state.dailySummaries.sort((a, b) => a.day - b.day);
}

export function pushEvent(state, event) {
  state.ui.eventCounter += 1;
  state.events.unshift({
    id: state.ui.eventCounter,
    day: event.day ?? state.game.currentDay,
    tick: event.tick ?? state.market.tick ?? state.game.currentTick,
    kind: event.kind || "system",
    title: event.title || "事件",
    detail: event.detail || "",
  });

  if (state.events.length > MAX_EVENTS) {
    state.events.length = MAX_EVENTS;
  }
}

export function clearSettlement(state) {
  state.ui.showSettlement = false;
}

function upsertNews(state, message) {
  const newsId = message.newsId ?? "";
  const existingIndex = state.news.items.findIndex((item) => item.newsId === newsId);
  const next = {
    newsId,
    content: message.content || "",
    publishTick: numberOr(message.publishTick, state.market.tick),
    day: state.game.currentDay,
    isFake: Boolean(message.isFake),
    sourcePlayer: message.sourcePlayer || "",
    receivedAt: state.game.currentTick || state.market.tick || 0,
  };

  if (existingIndex >= 0) {
    state.news.items.splice(existingIndex, 1);
  }
  state.news.items.unshift(next);
  if (state.news.items.length > MAX_NEWS) {
    state.news.items.length = MAX_NEWS;
  }
}

function upsertReportResult(state, message) {
  const newsId = message.newsId ?? "";
  if (newsId === "") return;
  state.news.results[newsId] = {
    playerToken: message.playerToken || "",
    prediction: message.prediction || "",
    isCorrect: Boolean(message.isCorrect),
    reward: numberOr(message.reward, 0),
    actualChange: numberOr(message.actualChange, 0),
  };
}

function appendMarketSnapshot(state) {
  const snapshot = {
    game: { ...state.game },
    market: {
      bids: state.market.bids,
      asks: state.market.asks,
      lastPrice: state.market.lastPrice,
      midPrice: state.market.midPrice,
      volume: state.market.volume,
      tick: state.market.tick,
    },
  };

  state.market.history.push(snapshot);
  if (state.market.history.length > MAX_MARKET_HISTORY) {
    state.market.history.shift();
  }

  ingestMarketSnapshot(state.market.candleAccumulator, snapshot);
  state.market.candles = state.market.candleAccumulator.candles;
}

function normalizePlayerState(message) {
  return {
    playerId: numberOr(message.playerId, -1),
    mora: numberOr(message.mora, 0),
    frozenMora: numberOr(message.frozenMora, 0),
    gold: numberOr(message.gold, 0),
    frozenGold: numberOr(message.frozenGold, 0),
    lockedGold: numberOr(message.lockedGold, 0),
    nav: numberOr(message.nav, 0),
    activeCards: Array.isArray(message.activeCards) ? message.activeCards : [],
    pendingOrders: Array.isArray(message.pendingOrders) ? message.pendingOrders : [],
  };
}

function emptyPlayerState() {
  return {
    playerId: -1,
    mora: 0,
    frozenMora: 0,
    gold: 0,
    frozenGold: 0,
    lockedGold: 0,
    nav: 0,
    activeCards: [],
    pendingOrders: [],
  };
}

function numberOr(value, fallback) {
  const number = Number(value);
  return Number.isFinite(number) ? number : fallback;
}

export function playerDisplayName(state, playerId) {
  if (playerId === undefined || playerId === null || playerId < 0) return "-";
  if (playerId === state.player.playerId && state.connection.token) {
    return state.connection.token;
  }
  return `选手 ${playerId}`;
}
