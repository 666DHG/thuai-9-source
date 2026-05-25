export function helloMessage(role, token, adminSecret) {
  const message = {
    messageType: "HELLO",
    role,
    protocolVersion: "v1",
  };
  if (role === "player") {
    message.token = token;
  }
  if (role === "admin" && adminSecret) {
    message.adminSecret = adminSecret;
  }
  return message;
}

export function debugQueryMessage() {
  return { messageType: "DEBUG_QUERY" };
}

export function debugGiveCardMessage(targetPlayerId, cardName) {
  const message = {
    messageType: "DEBUG_GIVE_CARD",
    cardName,
  };
  const id = toPlayerId(targetPlayerId);
  if (id !== undefined) {
    message.targetPlayerId = id;
  }
  return message;
}

export function debugInjectNewsMessage(sentiment, content) {
  const message = { messageType: "DEBUG_INJECT_NEWS", sentiment };
  if (content) message.content = content;
  return message;
}

export function debugAdvanceStageMessage() {
  return { messageType: "DEBUG_ADVANCE_STAGE" };
}

export function debugSetPlayerMessage(targetPlayerId, { mora, gold } = {}) {
  const message = { messageType: "DEBUG_SET_PLAYER" };
  const id = toPlayerId(targetPlayerId);
  if (id !== undefined) {
    message.targetPlayerId = id;
  }
  if (mora !== undefined && mora !== null && mora !== "") message.mora = Number(mora);
  if (gold !== undefined && gold !== null && gold !== "") message.gold = Number(gold);
  return message;
}

export function limitBuyMessage(token, price, quantity) {
  return {
    messageType: "LIMIT_BUY",
    token,
    price: toInteger(price),
    quantity: toInteger(quantity),
  };
}

export function limitSellMessage(token, price, quantity) {
  return {
    messageType: "LIMIT_SELL",
    token,
    price: toInteger(price),
    quantity: toInteger(quantity),
  };
}

export function cancelOrderMessage(token, orderId) {
  return {
    messageType: "CANCEL_ORDER",
    token,
    orderId: toInteger(orderId),
  };
}

export function submitReportMessage(token, newsId, prediction) {
  return {
    messageType: "SUBMIT_REPORT",
    token,
    newsId: toInteger(newsId),
    prediction,
  };
}

export function selectStrategyMessage(token, cardName) {
  return {
    messageType: "SELECT_STRATEGY",
    token,
    cardName,
  };
}

export function activateSkillMessage(token, skillName, targetPlayerId) {
  const message = {
    messageType: "ACTIVATE_SKILL",
    token,
    skillName,
  };
  const id = toPlayerId(targetPlayerId);
  if (id !== undefined) {
    message.targetPlayerId = id;
  }
  return message;
}

export function sendJson(ws, message) {
  if (!ws || ws.readyState !== 1) {
    throw new Error("WebSocket is not connected.");
  }
  ws.send(JSON.stringify(message));
}

function toInteger(value) {
  const number = Number.parseInt(value, 10);
  return Number.isFinite(number) ? number : 0;
}

function toPlayerId(value) {
  if (value === undefined || value === null || value === "") return undefined;
  const number = Number(value);
  if (!Number.isFinite(number) || number < 0 || !Number.isInteger(number)) return undefined;
  return number;
}
