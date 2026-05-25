#ifndef THUAI_PROTOCOL_HPP
#define THUAI_PROTOCOL_HPP

#include <cstdint>
#include <nlohmann/json.hpp>
#include <optional>
#include <string>

#include "models.hpp"

namespace thuai::protocol {

using json = nlohmann::json;

inline auto buildHelloMessage(
    const std::string& token, const std::string& role = "player",
    const std::optional<std::string>& adminSecret = std::nullopt) -> json {
  json message = {
      {"messageType", "HELLO"},
      {"token", token},
      {"role", role},
  };
  if (adminSecret.has_value()) {
    message["adminSecret"] = *adminSecret;
  }
  return message;
}

inline auto buildLimitBuyMessage(const std::string& token, std::int64_t price,
                                 int quantity) -> json {
  return {{"messageType", "LIMIT_BUY"},
          {"token", token},
          {"price", price},
          {"quantity", quantity}};
}

inline auto buildLimitSellMessage(const std::string& token, std::int64_t price,
                                  int quantity) -> json {
  return {{"messageType", "LIMIT_SELL"},
          {"token", token},
          {"price", price},
          {"quantity", quantity}};
}

inline auto buildCancelOrderMessage(const std::string& token,
                                    std::int64_t orderId) -> json {
  return {
      {"messageType", "CANCEL_ORDER"}, {"token", token}, {"orderId", orderId}};
}

inline auto buildSubmitReportMessage(const std::string& token, int newsId,
                                     Prediction prediction) -> json {
  return {{"messageType", "SUBMIT_REPORT"},
          {"token", token},
          {"newsId", newsId},
          {"prediction", predictionToString(prediction)}};
}

inline auto buildSelectStrategyMessage(const std::string& token,
                                       const std::string& cardName) -> json {
  return {{"messageType", "SELECT_STRATEGY"},
          {"token", token},
          {"cardName", cardName}};
}

inline auto buildActivateSkillMessage(
    const std::string& token, const std::string& skillName,
    const std::optional<int>& targetPlayerId = std::nullopt,
    const std::optional<std::string>& variant = std::nullopt) -> json {
  json message = {{"messageType", "ACTIVATE_SKILL"},
                  {"token", token},
                  {"skillName", skillName}};
  if (targetPlayerId.has_value()) {
    message["targetPlayerId"] = *targetPlayerId;
  }
  if (variant.has_value()) {
    message["variant"] = *variant;
  }
  return message;
}

inline auto parseGameState(const json& data) -> GameState {
  GameState state;
  state.stage = data.value("stage", "");
  state.currentMonth = data.value("currentMonth", 0);
  state.currentDay = data.value("currentDay", 0);
  state.currentTick = data.value("currentTick", 0);
  state.totalTicks = data.value("totalTicks", 0);
  if (data.contains("scores") && data["scores"].is_array()) {
    for (const auto& scoreEntry : data["scores"]) {
      PlayerScore playerScore;
      playerScore.playerId = scoreEntry.value("playerId", 0);
      playerScore.score = scoreEntry.value("score", 0);
      state.scores.push_back(playerScore);
    }
  }
  return state;
}

inline auto parseMarketState(const json& data) -> MarketState {
  MarketState state;
  if (data.contains("bids") && data["bids"].is_array()) {
    for (const auto& bidEntry : data["bids"]) {
      PriceLevel priceLevel;
      priceLevel.price = bidEntry.value("price", std::int64_t{0});
      priceLevel.quantity = bidEntry.value("quantity", 0);
      state.bids.push_back(priceLevel);
    }
  }
  if (data.contains("asks") && data["asks"].is_array()) {
    for (const auto& askEntry : data["asks"]) {
      PriceLevel priceLevel;
      priceLevel.price = askEntry.value("price", std::int64_t{0});
      priceLevel.quantity = askEntry.value("quantity", 0);
      state.asks.push_back(priceLevel);
    }
  }
  state.lastPrice = data.value("lastPrice", std::int64_t{0});
  state.midPrice = data.value("midPrice", std::int64_t{0});
  state.volume = data.value("volume", 0);
  state.tick = data.value("tick", 0);
  return state;
}

inline auto parsePlayerState(const json& data) -> PlayerState {
  PlayerState state;
  state.playerId = data.value("playerId", -1);
  state.mora = data.value("mora", std::int64_t{0});
  state.frozenMora = data.value("frozenMora", std::int64_t{0});
  state.gold = data.value("gold", 0);
  state.frozenGold = data.value("frozenGold", 0);
  state.lockedGold = data.value("lockedGold", 0);
  state.nav = data.value("nav", std::int64_t{0});
  state.networkDelay = data.value("networkDelay", 0);
  state.immediateOrdersUsedToday = data.value("immediateOrdersUsedToday", 0);
  state.restingOrdersUsedToday = data.value("restingOrdersUsedToday", 0);
  state.bonusImmediateOrdersToday = data.value("bonusImmediateOrdersToday", 0);
  state.monthlyTradeCount = data.value("monthlyTradeCount", 0);
  if (data.contains("activeCards") && data["activeCards"].is_array()) {
    for (const auto& activeCardValue : data["activeCards"]) {
      if (activeCardValue.is_string()) {
        state.activeCards.push_back(activeCardValue.get<std::string>());
      }
    }
  }
  if (data.contains("pendingOrders") && data["pendingOrders"].is_array()) {
    for (const auto& orderEntry : data["pendingOrders"]) {
      OrderInfo orderInfo;
      orderInfo.orderId = orderEntry.value("orderId", std::int64_t{0});
      orderInfo.arrivalTick = orderEntry.value("arrivalTick", 0);
      orderInfo.side = orderEntry.value("side", "");
      orderInfo.price = orderEntry.value("price", std::int64_t{0});
      orderInfo.quantity = orderEntry.value("quantity", 0);
      orderInfo.remainingQuantity = orderEntry.value("remainingQuantity", 0);
      orderInfo.status = orderEntry.value("status", "");
      orderInfo.intent = orderEntry.value("intent", "");
      state.pendingOrders.push_back(orderInfo);
    }
  }
  return state;
}

inline auto parseNews(const json& data) -> News {
  News news;
  news.month = data.value("month", 0);
  news.day = data.value("day", 0);
  news.newsId = data.value("newsId", 0);
  news.content = data.value("content", "");
  news.publishTick = data.value("publishTick", 0);
  return news;
}

inline auto parseReportResult(const json& data) -> ReportResult {
  ReportResult reportResult;
  reportResult.newsId = data.value("newsId", 0);
  reportResult.submissionRank = data.value("submissionRank", 0);
  reportResult.submitTick = data.value("submitTick", 0);
  reportResult.settlementTick = data.value("settlementTick", 0);
  reportResult.prediction = data.value("prediction", "");
  reportResult.isCorrect = data.value("isCorrect", false);
  reportResult.reward = data.value("reward", std::int64_t{0});
  reportResult.actualChange = data.value("actualChange", std::int64_t{0});
  return reportResult;
}

inline auto parseStrategyOptions(const json& data) -> StrategyOptions {
  StrategyOptions options;
  auto parseCard = [](const json& cardData) -> std::optional<CardOption> {
    if (cardData.is_null() || !cardData.is_object()) {
      return std::nullopt;
    }
    CardOption card;
    card.name = cardData.value("name", "");
    card.description = cardData.value("description", "");
    card.category = cardData.value("category", "");
    return card;
  };
  if (data.contains("infrastructure")) {
    options.infrastructure = parseCard(data["infrastructure"]);
  }
  if (data.contains("riskControl")) {
    options.riskControl = parseCard(data["riskControl"]);
  }
  if (data.contains("finTech")) {
    options.finTech = parseCard(data["finTech"]);
  }
  return options;
}

inline auto parseTrade(const json& data) -> TradeNotification {
  TradeNotification tradeNotification;
  tradeNotification.tradeId = data.value("tradeId", std::int64_t{0});
  tradeNotification.orderId = data.value("orderId", std::int64_t{0});
  tradeNotification.price = data.value("price", std::int64_t{0});
  tradeNotification.quantity = data.value("quantity", 0);
  tradeNotification.side = data.value("side", "");
  tradeNotification.fee = data.value("fee", std::int64_t{0});
  return tradeNotification;
}

inline auto parseSkillEffect(const json& data) -> SkillEffect {
  SkillEffect skillEffect;
  skillEffect.skillName = data.value("skillName", "");
  skillEffect.sourcePlayerId = data.value("sourcePlayerId", -1);
  if (data.contains("targetPlayerId") && !data["targetPlayerId"].is_null()) {
    skillEffect.targetPlayerId = data.value("targetPlayerId", -1);
  }
  skillEffect.description = data.value("description", "");
  return skillEffect;
}

inline auto parseDaySettlement(const json& data) -> DaySettlement {
  DaySettlement settlement;
  settlement.day = data.value("day", 0);
  settlement.month = data.value("month", 0);
  settlement.winnerPlayerId = data.value("winnerPlayerId", -1);
  settlement.reason = data.value("reason", "");
  settlement.finalBonusWinnerPlayerId =
      data.value("finalBonusWinnerPlayerId", -1);
  settlement.finalBonusPoints = data.value("finalBonusPoints", 0);

  if (data.contains("players") && data["players"].is_array()) {
    for (const auto& playerEntry : data["players"]) {
      DaySettlementPlayer player;
      player.playerId = playerEntry.value("playerId", -1);
      player.nav = playerEntry.value("nav", std::int64_t{0});
      player.mora = playerEntry.value("mora", std::int64_t{0});
      player.gold = playerEntry.value("gold", 0);
      player.frozenMora = playerEntry.value("frozenMora", std::int64_t{0});
      player.frozenGold = playerEntry.value("frozenGold", 0);
      player.lockedGold = playerEntry.value("lockedGold", 0);
      player.tradeCount = playerEntry.value("tradeCount", 0);
      if (playerEntry.contains("activeCards") &&
          playerEntry["activeCards"].is_array()) {
        for (const auto& cardValue : playerEntry["activeCards"]) {
          if (cardValue.is_string()) {
            player.activeCards.push_back(cardValue.get<std::string>());
          }
        }
      }
      settlement.players.push_back(std::move(player));
    }
  }

  if (data.contains("cumulativeNavs") && data["cumulativeNavs"].is_object()) {
    for (const auto& [key, navValue] : data["cumulativeNavs"].items()) {
      settlement.cumulativeNavs[std::stoi(key)] = navValue.get<std::int64_t>();
    }
  }

  return settlement;
}

}  // namespace thuai::protocol

#endif  // THUAI_PROTOCOL_HPP
