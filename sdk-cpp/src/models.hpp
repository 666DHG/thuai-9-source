#pragma once

#include <cstdint>
#include <map>
#include <optional>
#include <string>
#include <vector>

namespace thuai {

struct PriceLevel {
  std::int64_t price = 0;
  int quantity = 0;
};

struct OrderInfo {
  std::int64_t orderId = 0;
  int arrivalTick = 0;
  std::string side;
  std::int64_t price = 0;
  int quantity = 0;
  int remainingQuantity = 0;
  std::string status;
  std::string intent;
};

struct CardOption {
  std::string name;
  std::string description;
  std::string category;
};

struct PlayerScore {
  int playerId = 0;
  int score = 0;
};

struct GameState {
  std::string stage;
  int currentMonth = 0;
  int currentDay = 0;
  int currentTick = 0;
  int totalTicks = 0;
  std::vector<PlayerScore> scores;
};

struct MarketState {
  std::vector<PriceLevel> bids;
  std::vector<PriceLevel> asks;
  std::int64_t lastPrice = 0;
  std::int64_t midPrice = 0;
  int volume = 0;
  int tick = 0;
};

struct PlayerState {
  int playerId = -1;
  std::int64_t mora = 0;
  std::int64_t frozenMora = 0;
  int gold = 0;
  int frozenGold = 0;
  int lockedGold = 0;
  std::int64_t nav = 0;
  int networkDelay = 0;
  int immediateOrdersUsedToday = 0;
  int restingOrdersUsedToday = 0;
  int bonusImmediateOrdersToday = 0;
  int monthlyTradeCount = 0;
  std::vector<std::string> activeCards;
  std::vector<OrderInfo> pendingOrders;
};

struct News {
  int month = 0;
  int day = 0;
  int newsId = 0;
  std::string content;
  int publishTick = 0;
};

struct ReportResult {
  int newsId = 0;
  int submissionRank = 0;
  int submitTick = 0;
  int settlementTick = 0;
  std::string prediction;
  bool isCorrect = false;
  std::int64_t reward = 0;
  std::int64_t actualChange = 0;
};

struct StrategyOptions {
  std::optional<CardOption> infrastructure;
  std::optional<CardOption> riskControl;
  std::optional<CardOption> finTech;
};

struct TradeNotification {
  std::int64_t tradeId = 0;
  std::int64_t orderId = 0;
  std::int64_t price = 0;
  int quantity = 0;
  std::string side;
  std::int64_t fee = 0;
};

struct SkillEffect {
  std::string skillName;
  int sourcePlayerId = -1;
  std::optional<int> targetPlayerId;
  std::string description;
};

struct DaySettlementPlayer {
  int playerId = -1;
  std::int64_t nav = 0;
  std::int64_t mora = 0;
  int gold = 0;
  std::int64_t frozenMora = 0;
  int frozenGold = 0;
  int lockedGold = 0;
  int tradeCount = 0;
  std::vector<std::string> activeCards;
};

struct DaySettlement {
  int day = 0;
  int month = 0;
  int winnerPlayerId = -1;
  std::string reason;
  std::vector<DaySettlementPlayer> players;
  std::map<int, std::int64_t> cumulativeNavs;
  int finalBonusWinnerPlayerId = -1;
  int finalBonusPoints = 0;
};

enum class Prediction { Long, Short, Hold };

inline auto predictionToString(Prediction prediction) -> std::string {
  switch (prediction) {
    case Prediction::Long:
      return "Long";
    case Prediction::Short:
      return "Short";
    case Prediction::Hold:
      return "Hold";
  }
  return "Hold";
}

}  // namespace thuai
