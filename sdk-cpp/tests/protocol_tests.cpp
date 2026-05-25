#define DOCTEST_CONFIG_IMPLEMENT_WITH_MAIN
#include <doctest/doctest.h>

#include "protocol.hpp"

using nlohmann::json;
using thuai::GameState;
using thuai::Prediction;
using thuai::protocol::buildActivateSkillMessage;
using thuai::protocol::buildHelloMessage;
using thuai::protocol::buildLimitBuyMessage;
using thuai::protocol::buildSubmitReportMessage;
using thuai::protocol::parseDaySettlement;
using thuai::protocol::parseGameState;
using thuai::protocol::parseNews;
using thuai::protocol::parsePlayerState;
using thuai::protocol::parseReportResult;
using thuai::protocol::parseSkillEffect;
using thuai::protocol::parseStrategyOptions;

namespace {
// NOLINTBEGIN
TEST_CASE("protocol builders serialize outbound actions") {
  json hello = buildHelloMessage("player-1");
  CHECK(hello["messageType"] == "HELLO");
  CHECK(hello["token"] == "player-1");
  CHECK(hello["role"] == "player");
  CHECK_FALSE(hello.contains("adminSecret"));

  json limit_buy = buildLimitBuyMessage("player-1", 1050, 3);
  CHECK(limit_buy["messageType"] == "LIMIT_BUY");
  CHECK(limit_buy["token"] == "player-1");
  CHECK(limit_buy["price"] == 1050);
  CHECK(limit_buy["quantity"] == 3);

  json activate_skill = buildActivateSkillMessage("player-1", "MarketRadar");
  CHECK(activate_skill["messageType"] == "ACTIVATE_SKILL");
  CHECK(activate_skill["token"] == "player-1");
  CHECK(activate_skill["skillName"] == "MarketRadar");
  CHECK_FALSE(activate_skill.contains("targetPlayerId"));
  CHECK_FALSE(activate_skill.contains("variant"));

  json targeted_skill = buildActivateSkillMessage("player-1", "Freeze", 1,
                                                  std::string("intense"));
  CHECK(targeted_skill["targetPlayerId"] == 1);
  CHECK(targeted_skill["variant"] == "intense");

  json report = buildSubmitReportMessage("player-3", 8, Prediction::Short);
  CHECK(report["messageType"] == "SUBMIT_REPORT");
  CHECK(report["newsId"] == 8);
  CHECK(report["prediction"] == "Short");
}

TEST_CASE("game state parser reads current protocol fields") {
  GameState state = parseGameState(json{
      {"stage", "TradingDay"},
      {"currentMonth", 4},
      {"currentDay", 2},
      {"currentTick", 88},
      {"totalTicks", 300},
      {"scores", json::array({
                     json{{"playerId", 0}, {"score", 13}},
                     json{{"playerId", 1}, {"score", 11}},
                 })},
  });

  CHECK(state.stage == "TradingDay");
  CHECK(state.currentMonth == 4);
  CHECK(state.currentDay == 2);
  CHECK(state.currentTick == 88);
  CHECK(state.totalTicks == 300);
  REQUIRE(state.scores.size() == 2);
  CHECK(state.scores[0].playerId == 0);
  CHECK(state.scores[0].score == 13);
}

TEST_CASE("player state parser handles nested orders and active cards") {
  thuai::PlayerState state = parsePlayerState(json{
      {"playerId", 0},
      {"mora", 1400},
      {"frozenMora", 120},
      {"gold", 9},
      {"frozenGold", 3},
      {"lockedGold", 1},
      {"nav", 1520},
      {"networkDelay", 50},
      {"immediateOrdersUsedToday", 2},
      {"restingOrdersUsedToday", 5},
      {"bonusImmediateOrdersToday", 1},
      {"monthlyTradeCount", 17},
      {"activeCards", json::array({"Bridge", "Firewall"})},
      {"pendingOrders", json::array({
                            json{
                                {"orderId", 42},
                                {"arrivalTick", 18},
                                {"side", "Buy"},
                                {"price", 990},
                                {"quantity", 6},
                                {"remainingQuantity", 4},
                                {"status", "Active"},
                                {"intent", "Resting"},
                            },
                        })},
  });

  CHECK(state.playerId == 0);
  CHECK(state.mora == 1400);
  CHECK(state.monthlyTradeCount == 17);
  REQUIRE(state.activeCards.size() == 2);
  CHECK(state.activeCards[0] == "Bridge");
  REQUIRE(state.pendingOrders.size() == 1);
  CHECK(state.pendingOrders[0].orderId == 42);
  CHECK(state.pendingOrders[0].arrivalTick == 18);
  CHECK(state.pendingOrders[0].intent == "Resting");
}

TEST_CASE("news and report parsers preserve wire values") {
  thuai::News news = parseNews(json{
      {"month", 6},
      {"day", 1},
      {"newsId", 12},
      {"content", "Supply tightened"},
      {"publishTick", 160},
  });
  CHECK(news.month == 6);
  CHECK(news.day == 1);
  CHECK(news.newsId == 12);
  CHECK(news.content == "Supply tightened");
  CHECK(news.publishTick == 160);

  thuai::ReportResult result = parseReportResult(json{
      {"newsId", 12},
      {"submissionRank", 1},
      {"submitTick", 161},
      {"settlementTick", 240},
      {"prediction", "Long"},
      {"isCorrect", true},
      {"reward", 280},
      {"actualChange", 70},
  });
  CHECK(result.submissionRank == 1);
  CHECK(result.isCorrect);
  CHECK(result.reward == 280);
  CHECK(result.actualChange == 70);
}

TEST_CASE("strategy and skill parsers handle optional fields") {
  thuai::StrategyOptions options = parseStrategyOptions(json{
      {"infrastructure",
       json{
           {"name", "Tunnel"},
           {"description", "Reduces latency"},
           {"category", "Infrastructure"},
       }},
      {"riskControl", nullptr},
      {"finTech",
       json{
           {"name", "Flash"},
           {"description", "Adds extra order quota"},
           {"category", "FinTech"},
       }},
  });
  REQUIRE(options.infrastructure.has_value());
  CHECK(options.infrastructure->name == "Tunnel");
  CHECK_FALSE(options.riskControl.has_value());
  REQUIRE(options.finTech.has_value());
  CHECK(options.finTech->name == "Flash");

  thuai::SkillEffect effect = parseSkillEffect(json{
      {"skillName", "Hedge"},
      {"sourcePlayerId", 0},
      {"description", "Protected against one loss"},
  });
  CHECK(effect.skillName == "Hedge");
  CHECK(effect.sourcePlayerId == 0);
  CHECK_FALSE(effect.targetPlayerId.has_value());
  CHECK(effect.description == "Protected against one loss");
}

TEST_CASE("day settlement parser preserves month summary fields") {
  thuai::DaySettlement settlement = parseDaySettlement(json{
      {"day", 30},
      {"month", 6},
      {"winnerPlayerId", 0},
      {"reason", "highest NAV"},
      {"players", json::array({
                      json{
                          {"playerId", 0},
                          {"nav", 2000000},
                          {"mora", 1001000},
                          {"gold", 999},
                          {"frozenMora", 0},
                          {"frozenGold", 0},
                          {"lockedGold", 0},
                          {"tradeCount", 2},
                          {"activeCards", json::array({"内幕消息"})},
                      },
                  })},
      {"cumulativeNavs",
       json{
           {"0", 4000000},
       }},
      {"finalBonusWinnerPlayerId", 1},
      {"finalBonusPoints", 2},
  });

  CHECK(settlement.day == 30);
  CHECK(settlement.month == 6);
  CHECK(settlement.winnerPlayerId == 0);
  CHECK(settlement.reason == "highest NAV");
  REQUIRE(settlement.players.size() == 1);
  CHECK(settlement.players[0].playerId == 0);
  CHECK(settlement.players[0].tradeCount == 2);
  REQUIRE(settlement.players[0].activeCards.size() == 1);
  CHECK(settlement.players[0].activeCards[0] == "内幕消息");
  REQUIRE(settlement.cumulativeNavs.size() == 1);
  CHECK(settlement.cumulativeNavs[0] == 4000000);
  CHECK(settlement.finalBonusWinnerPlayerId == 1);
  CHECK(settlement.finalBonusPoints == 2);
}
// NOLINTEND
}  // namespace
