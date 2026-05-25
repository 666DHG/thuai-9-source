#pragma once

#include <ixwebsocket/IXWebSocket.h>
#include <spdlog/spdlog.h>

#include <atomic>
#include <chrono>
#include <cstdint>
#include <nlohmann/json.hpp>
#include <optional>
#include <string>
#include <thread>
#include <utility>

#include "models.hpp"
#include "protocol.hpp"

namespace thuai {

using json = nlohmann::json;

inline constexpr int kWaitingTimeMs = 100;

class Agent {
 public:
  explicit Agent(std::string token,
                 std::string serverUrl = "ws://localhost:14514")
      : token_(std::move(token)), serverUrl_(std::move(serverUrl)) {}

  virtual ~Agent() = default;

  Agent(const Agent&) = delete;
  Agent(Agent&&) = delete;
  auto operator=(const Agent&) -> Agent& = delete;
  auto operator=(Agent&&) -> Agent& = delete;

  // --- Actions ---
  void limitBuy(std::int64_t price, int quantity) {
    spdlog::debug("Queueing limit buy price={} quantity={}", price, quantity);
    send(protocol::buildLimitBuyMessage(token_, price, quantity));
  }

  void limitSell(std::int64_t price, int quantity) {
    spdlog::debug("Queueing limit sell price={} quantity={}", price, quantity);
    send(protocol::buildLimitSellMessage(token_, price, quantity));
  }

  void cancelOrder(std::int64_t orderId) {
    spdlog::debug("Queueing cancel orderId={}", orderId);
    send(protocol::buildCancelOrderMessage(token_, orderId));
  }

  void submitReport(int newsId, Prediction prediction) {
    spdlog::debug("Queueing report newsId={} prediction={}", newsId,
                  predictionToString(prediction));
    send(protocol::buildSubmitReportMessage(token_, newsId, prediction));
  }

  void selectStrategy(const std::string& cardName) {
    spdlog::debug("Queueing strategy selection card={}", cardName);
    send(protocol::buildSelectStrategyMessage(token_, cardName));
  }

  void activateSkill(const std::string& skillName, int targetPlayerId = -1,
                     const std::string& variant = "") {
    spdlog::debug(
        "Queueing skill activation name={} targetPlayerId={} variant={}",
        skillName, targetPlayerId, variant.empty() ? "none" : variant);
    send(protocol::buildActivateSkillMessage(
        token_, skillName,
        targetPlayerId >= 0 ? std::make_optional(targetPlayerId) : std::nullopt,
        variant.empty() ? std::nullopt : std::make_optional(variant)));
  }

  auto getAllPlayerIds() const -> std::vector<int> {
    std::vector<int> ids;
    for (const auto& score : gameState.scores) {
      ids.push_back(score.playerId);
    }
    return ids;
  }

  // --- State ---
  GameState gameState{};
  MarketState marketState{};
  PlayerState playerState{};
  std::optional<News> latestNews{};
  std::optional<StrategyOptions> strategyOptions{};
  std::optional<DaySettlement> latestDaySettlement{};

  // --- Event Handlers (override what you need) ---
  virtual void onGameState(const GameState&) {}
  virtual void onMarketState(const MarketState&) {}
  virtual void onPlayerState(const PlayerState&) {}
  virtual void onNews(const News&) {}
  virtual void onReportResult(const ReportResult&) {}
  virtual void onStrategyOptions(const StrategyOptions&) {}
  virtual void onTrade(const TradeNotification&) {}
  virtual void onSkillEffect(const SkillEffect&) {}
  virtual void onDaySettlement(const DaySettlement&) {}
  virtual void onError(int, const std::string&) {}

  // --- Run ---
  void run() {
    std::atomic_bool closed{false};

    ws_.setUrl(serverUrl_);
    ws_.setOnMessageCallback(
        [this, &closed](const ix::WebSocketMessagePtr& msg) {
          if (msg->type == ix::WebSocketMessageType::Message) {
            handleMessage(msg->str);
            return;
          }

          if (msg->type == ix::WebSocketMessageType::Open) {
            spdlog::info("Connected to {}", serverUrl_);
            send(protocol::buildHelloMessage(token_));
            return;
          }

          if (msg->type == ix::WebSocketMessageType::Close) {
            spdlog::info("Disconnected from {}", serverUrl_);
            closed = true;
            return;
          }

          if (msg->type == ix::WebSocketMessageType::Error) {
            spdlog::error("WebSocket error: {}", msg->errorInfo.reason);
            closed = true;
          }
        });
    ws_.start();

    while (!closed) {
      std::this_thread::sleep_for(std::chrono::milliseconds(kWaitingTimeMs));
      if (gameState.stage == "Finished") {
        spdlog::info("Game stage is Finished, stopping agent loop");
        break;
      }
    }

    ws_.stop();
    spdlog::info("Agent loop stopped");
  }

 private:
  std::string token_;
  std::string serverUrl_;
  ix::WebSocket ws_;

  void send(const json& data) {
    const auto payload = data.dump();
    spdlog::debug("Sending messageType={} payload={}",
                  data.value("messageType", std::string("UNKNOWN")), payload);
    ws_.send(payload);
  }

  void handleMessage(const std::string& raw) {
    try {
      spdlog::debug("Received raw payload={}", raw);

      const auto data = json::parse(raw);
      const std::string msgType = data.value("messageType", "");

      if (msgType.empty()) {
        spdlog::warn("Received message without messageType payload={}", raw);
        return;
      }

      if (msgType == "GAME_STATE") {
        gameState = protocol::parseGameState(data);
        spdlog::debug(
            "Parsed game state stage={} month={} day={} tick={}/{} "
            "scoreEntries={}",
            gameState.stage, gameState.currentMonth, gameState.currentDay,
            gameState.currentTick, gameState.totalTicks,
            gameState.scores.size());
        onGameState(gameState);
      } else if (msgType == "MARKET_STATE") {
        marketState = protocol::parseMarketState(data);
        spdlog::debug(
            "Parsed market state tick={} bids={} asks={} lastPrice={} "
            "midPrice={} volume={}",
            marketState.tick, marketState.bids.size(), marketState.asks.size(),
            marketState.lastPrice, marketState.midPrice, marketState.volume);
        onMarketState(marketState);
      } else if (msgType == "PLAYER_STATE") {
        playerState = protocol::parsePlayerState(data);
        spdlog::debug(
            "Parsed player state mora={} frozenMora={} gold={} frozenGold={} "
            "lockedGold={} nav={} pendingOrders={} activeCards={}",
            playerState.mora, playerState.frozenMora, playerState.gold,
            playerState.frozenGold, playerState.lockedGold, playerState.nav,
            playerState.pendingOrders.size(), playerState.activeCards.size());
        onPlayerState(playerState);
      } else if (msgType == "NEWS_BROADCAST") {
        latestNews = protocol::parseNews(data);
        spdlog::debug("Parsed news newsId={} month={} day={} publishTick={}",
                      latestNews->newsId, latestNews->month, latestNews->day,
                      latestNews->publishTick);
        onNews(*latestNews);
      } else if (msgType == "REPORT_RESULT") {
        const auto result = protocol::parseReportResult(data);
        spdlog::debug(
            "Parsed report result newsId={} prediction={} correct={} reward={}",
            result.newsId, result.prediction, result.isCorrect, result.reward);
        onReportResult(result);
      } else if (msgType == "STRATEGY_OPTIONS") {
        strategyOptions = protocol::parseStrategyOptions(data);
        spdlog::debug(
            "Parsed strategy options infrastructure={} riskControl={} "
            "finTech={}",
            strategyOptions->infrastructure.has_value(),
            strategyOptions->riskControl.has_value(),
            strategyOptions->finTech.has_value());
        onStrategyOptions(*strategyOptions);
      } else if (msgType == "TRADE_NOTIFICATION") {
        const auto trade = protocol::parseTrade(data);
        spdlog::debug(
            "Parsed trade tradeId={} orderId={} side={} price={} quantity={} "
            "fee={}",
            trade.tradeId, trade.orderId, trade.side, trade.price,
            trade.quantity, trade.fee);
        onTrade(trade);
      } else if (msgType == "SKILL_EFFECT") {
        const auto effect = protocol::parseSkillEffect(data);
        spdlog::debug(
            "Parsed skill effect skill={} sourcePlayerId={} targetPlayerId={} "
            "description={}",
            effect.skillName, effect.sourcePlayerId,
            effect.targetPlayerId.has_value()
                ? std::to_string(*effect.targetPlayerId)
                : "none",
            effect.description);
        onSkillEffect(effect);
      } else if (msgType == "DAY_SETTLEMENT") {
        latestDaySettlement = protocol::parseDaySettlement(data);
        spdlog::debug(
            "Parsed day settlement month={} day={} winnerPlayerId={} "
            "players={}",
            latestDaySettlement->month, latestDaySettlement->day,
            latestDaySettlement->winnerPlayerId,
            latestDaySettlement->players.size());
        onDaySettlement(*latestDaySettlement);
      } else if (msgType == "ERROR") {
        const int code = data.value("errorCode", 0);
        const std::string message = data.value("message", std::string(""));
        spdlog::warn("Server error message code={} message={}", code, message);
        onError(code, message);
      } else {
        spdlog::warn("Ignoring unknown messageType={} payload={}", msgType,
                     raw);
      }
    } catch (const std::exception& e) {
      spdlog::error("Failed to parse inbound message: {} payload={}", e.what(),
                    raw);
    }
  }
};

}  // namespace thuai
