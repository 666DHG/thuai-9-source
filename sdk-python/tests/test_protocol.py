"""Protocol contract tests for the Python SDK."""

from __future__ import annotations

import json
import unittest
from typing import TypeAlias
from unittest.mock import AsyncMock, patch

from sdk_python.message import (
    _optional_object,
    _optional_str,
    _require_bool,
    _require_int,
    _require_list,
    _require_object_list,
    _require_str,
    _require_str_list,
    parse_error_message,
    parse_game_state,
    parse_inbound_message,
    parse_market_state,
    parse_news,
    parse_player_state,
    parse_report_result,
    parse_skill_effect,
    parse_strategy_options,
    parse_trade_notification,
)
from sdk_python.models import Prediction
from sdk_python.agent import Agent

ProtocolMessage: TypeAlias = dict[str, object]


class CapturingAgent(Agent):
    """Agent subclass that records outgoing payloads for assertion."""

    def __init__(self) -> None:
        super().__init__("player-1")
        self.sent_messages: list[ProtocolMessage] = []

    async def _send(self, data: ProtocolMessage) -> None:
        self.sent_messages.append(data)


class FakeWebSocket:
    """Simple async websocket double for protocol-flow tests."""

    def __init__(self, messages: list[str]) -> None:
        self._messages = messages
        self._index = 0
        self.sent_messages: list[str] = []
        self.closed = False

    async def send(self, data: str) -> None:
        """simulating send data"""
        self.sent_messages.append(data)

    async def close(self) -> None:
        """simulating close"""
        self.closed = True

    def __aiter__(self) -> "FakeWebSocket":
        return self

    async def __anext__(self) -> str:
        if self._index >= len(self._messages):
            raise StopAsyncIteration
        message = self._messages[self._index]
        self._index += 1
        return message


class RecordingAgent(Agent):
    """Agent subclass that records dispatched callbacks."""

    def __init__(self) -> None:
        super().__init__("player-1")
        self.events: list[tuple[object, ...]] = []

    async def on_game_state(self, state) -> None:  # type: ignore[override]
        self.events.append(("game", state.current_month))

    async def on_market_state(self, state) -> None:  # type: ignore[override]
        self.events.append(("market", state.tick))

    async def on_player_state(self, state) -> None:  # type: ignore[override]
        self.events.append(("player", state.mora))

    async def on_news(self, news) -> None:  # type: ignore[override]
        self.events.append(("news", news.news_id))

    async def on_report_result(self, result) -> None:  # type: ignore[override]
        self.events.append(("report", result.reward))

    async def on_strategy_options(self, options) -> None:  # type: ignore[override]
        self.events.append(("strategy", options.infrastructure.name))

    async def on_trade(self, trade) -> None:  # type: ignore[override]
        self.events.append(("trade", trade.trade_id))

    async def on_skill_effect(self, effect) -> None:  # type: ignore[override]
        self.events.append(("skill", effect.skill_name))

    async def on_error(self, code: int, message: str) -> None:  # type: ignore[override]
        self.events.append(("error", code, message))


class ProtocolParserTests(unittest.TestCase):
    """Parser coverage for the Python SDK wire snapshots."""

    def test_parse_game_state_matches_wire_fields(self) -> None:
        """Game-state parsing should preserve the documented wire fields."""

        state = parse_game_state(
            {
                "stage": "TradingDay",
                "currentMonth": 4,
                "currentDay": 2,
                "currentTick": 73,
                "totalTicks": 300,
                "scores": [
                    {"playerId": 0, "score": 12},
                    {"playerId": 1, "score": 8},
                ],
            }
        )

        self.assertEqual("TradingDay", state.stage)
        self.assertEqual(4, state.current_month)
        self.assertEqual(2, state.current_day)
        self.assertEqual(73, state.current_tick)
        self.assertEqual(300, state.total_ticks)
        self.assertEqual(
            [(0, 12), (1, 8)],
            [(score.player_id, score.score) for score in state.scores],
        )

    def test_parse_player_state_reads_nested_orders(self) -> None:
        """Player-state parsing should preserve nested orders and quotas."""

        state = parse_player_state(
            {
                "playerId": 0,
                "mora": 1200,
                "frozenMora": 150,
                "gold": 7,
                "frozenGold": 2,
                "lockedGold": 1,
                "nav": 1330,
                "networkDelay": 45,
                "immediateOrdersUsedToday": 3,
                "restingOrdersUsedToday": 5,
                "bonusImmediateOrdersToday": 1,
                "monthlyTradeCount": 14,
                "activeCards": ["Bridge", "Firewall"],
                "pendingOrders": [
                    {
                        "orderId": 88,
                        "arrivalTick": 19,
                        "side": "Sell",
                        "price": 1030,
                        "quantity": 4,
                        "remainingQuantity": 1,
                        "status": "PartiallyFilled",
                        "intent": "Resting",
                    }
                ],
            }
        )

        self.assertEqual(0, state.player_id)
        self.assertEqual(1200, state.mora)
        self.assertEqual(150, state.frozen_mora)
        self.assertEqual(14, state.monthly_trade_count)
        self.assertEqual(["Bridge", "Firewall"], state.active_cards)
        self.assertEqual(1, len(state.pending_orders))

        order = state.pending_orders[0]
        self.assertEqual(88, order.order_id)
        self.assertEqual(19, order.arrival_tick)
        self.assertEqual("Sell", order.side)
        self.assertEqual(1030, order.price)
        self.assertEqual(4, order.quantity)
        self.assertEqual(1, order.remaining_quantity)
        self.assertEqual("PartiallyFilled", order.status)
        self.assertEqual("Resting", order.intent)

    def test_parse_optional_protocol_messages(self) -> None:
        """Optional broadcast payloads should map into the expected SDK models."""

        news = parse_news(
            {
                "month": 5,
                "day": 1,
                "newsId": 9,
                "content": "Macro outlook improved",
                "publishTick": 101,
            }
        )
        self.assertEqual(
            (5, 1, 9, "Macro outlook improved", 101),
            (news.month, news.day, news.news_id, news.content, news.publish_tick),
        )

        report = parse_report_result(
            {
                "newsId": 9,
                "submissionRank": 2,
                "submitTick": 104,
                "settlementTick": 180,
                "prediction": "Long",
                "isCorrect": True,
                "reward": 240,
                "actualChange": 60,
            }
        )
        self.assertEqual(2, report.submission_rank)
        self.assertTrue(report.is_correct)
        self.assertEqual(240, report.reward)

        effect = parse_skill_effect(
            {
                "skillName": "Hedge",
                "sourcePlayerId": 0,
                "targetPlayerId": None,
                "description": "Protected against one loss",
            }
        )
        self.assertEqual("Hedge", effect.skill_name)
        self.assertEqual(0, effect.source_player_id)
        self.assertIsNone(effect.target_player_id)

        options = parse_strategy_options(
            {
                "infrastructure": {
                    "name": "Bridge",
                    "description": "Boosts capacity",
                    "category": "Infrastructure",
                },
                "riskControl": None,
                "finTech": {
                    "name": "Flash",
                    "description": "Extra order quota",
                    "category": "FinTech",
                },
            }
        )
        self.assertEqual("Bridge", options.infrastructure.name)
        self.assertIsNone(options.risk_control)
        self.assertEqual("Flash", options.fin_tech.name)

    def test_parse_additional_protocol_messages(self) -> None:
        """Remaining wire messages should parse into the documented models."""

        market = parse_market_state(
            {
                "bids": [{"price": 1001, "quantity": 2}],
                "asks": [{"price": 1005, "quantity": 4}],
                "lastPrice": 1003,
                "midPrice": 1002,
                "volume": 18,
                "tick": 77,
            }
        )
        self.assertEqual(1001, market.bids[0].price)
        self.assertEqual(1005, market.asks[0].price)

        trade = parse_trade_notification(
            {
                "tradeId": 11,
                "orderId": 88,
                "price": 1030,
                "quantity": 4,
                "side": "Sell",
                "fee": 3,
            }
        )
        self.assertEqual(
            (11, 88, 1030, 4, "Sell", 3),
            (
                trade.trade_id,
                trade.order_id,
                trade.price,
                trade.quantity,
                trade.side,
                trade.fee,
            ),
        )

        code, message = parse_error_message({"errorCode": 404, "message": "boom"})
        self.assertEqual((404, "boom"), (code, message))

        msg_type, payload = parse_inbound_message(
            {
                "messageType": "TRADE_NOTIFICATION",
                "tradeId": 11,
                "orderId": 88,
                "price": 1030,
                "quantity": 4,
                "side": "Sell",
                "fee": 3,
            }
        )
        self.assertEqual("TRADE_NOTIFICATION", msg_type)
        self.assertEqual(88, payload["orderId"])

    def test_message_helpers_reject_invalid_types(self) -> None:
        """Validation helpers should raise on type mismatches."""

        cases = [
            (_require_int, {"value": True}, "value"),
            (_require_bool, {"value": 1}, "value"),
            (_require_str, {"value": 1}, "value"),
            (_require_list, {"value": 1}, "value"),
            (_optional_str, {"value": 1}, "value"),
            (_optional_object, {"value": 1}, "value"),
            (_require_str_list, {"value": [1]}, "value"),
            (_require_object_list, {"value": [1]}, "value"),
        ]

        for helper, payload, key in cases:
            with self.subTest(helper=helper.__name__):
                with self.assertRaises(TypeError):
                    helper(payload, key)

        with self.assertRaises(TypeError):
            parse_inbound_message([])

    def test_update_state_tracks_latest_protocol_snapshots(self) -> None:
        """State caches should update when snapshot messages are received."""

        agent = Agent("player-1")

        agent._update_state(  # pylint: disable=protected-access
            "GAME_STATE",
            {
                "stage": "TradingDay",
                "currentMonth": 6,
                "currentDay": 3,
                "currentTick": 150,
                "totalTicks": 300,
                "scores": [{"playerId": 0, "score": 16}],
            },
        )
        agent._update_state(  # pylint: disable=protected-access
            "NEWS_BROADCAST",
            {
                "month": 6,
                "day": 3,
                "newsId": 15,
                "content": "Demand fell",
                "publishTick": 151,
            },
        )
        agent._update_state(  # pylint: disable=protected-access
            "STRATEGY_OPTIONS",
            {
                "infrastructure": {
                    "name": "Tunnel",
                    "description": "Reduces latency",
                    "category": "Infrastructure",
                },
                "riskControl": None,
                "finTech": None,
            },
        )

        self.assertEqual(6, agent.game_state.current_month)
        self.assertEqual("Demand fell", agent.latest_news.content)
        self.assertEqual("Tunnel", agent.strategy_options.infrastructure.name)

    def test_parse_inbound_message_reads_envelope(self) -> None:
        """Inbound messages should expose the type and validated payload."""

        msg_type, payload = parse_inbound_message(
            {
                "messageType": "NEWS_BROADCAST",
                "month": 5,
                "day": 1,
                "newsId": 9,
                "content": "Macro outlook improved",
                "publishTick": 101,
            }
        )

        self.assertEqual("NEWS_BROADCAST", msg_type)
        self.assertEqual(9, payload["newsId"])


class ProtocolActionTests(unittest.IsolatedAsyncioTestCase):
    """Outbound action coverage for the Python SDK."""

    async def test_run_dispatches_messages_and_closes_socket(self) -> None:
        """The event loop should connect, dispatch, and close cleanly."""

        messages = [
            "not json",
            json.dumps(
                {
                    "messageType": "GAME_STATE",
                    "stage": "TradingDay",
                    "currentMonth": 4,
                    "currentDay": 2,
                    "currentTick": 73,
                    "totalTicks": 300,
                    "scores": [{"playerId": 0, "score": 12}],
                }
            ),
            json.dumps(
                {
                    "messageType": "MARKET_STATE",
                    "bids": [{"price": 1001, "quantity": 2}],
                    "asks": [{"price": 1005, "quantity": 4}],
                    "lastPrice": 1003,
                    "midPrice": 1002,
                    "volume": 18,
                    "tick": 77,
                }
            ),
            json.dumps(
                {
                    "messageType": "PLAYER_STATE",
                    "playerId": 0,
                    "mora": 1200,
                    "frozenMora": 150,
                    "gold": 7,
                    "frozenGold": 2,
                    "lockedGold": 1,
                    "nav": 1330,
                    "networkDelay": 45,
                    "immediateOrdersUsedToday": 3,
                    "restingOrdersUsedToday": 5,
                    "bonusImmediateOrdersToday": 1,
                    "monthlyTradeCount": 14,
                    "activeCards": ["Bridge", "Firewall"],
                    "pendingOrders": [],
                }
            ),
            json.dumps(
                {
                    "messageType": "NEWS_BROADCAST",
                    "month": 5,
                    "day": 1,
                    "newsId": 9,
                    "content": "Macro outlook improved",
                    "publishTick": 101,
                }
            ),
            json.dumps(
                {
                    "messageType": "REPORT_RESULT",
                    "newsId": 9,
                    "submissionRank": 2,
                    "submitTick": 104,
                    "settlementTick": 180,
                    "prediction": "Long",
                    "isCorrect": True,
                    "reward": 240,
                    "actualChange": 60,
                }
            ),
            json.dumps(
                {
                    "messageType": "STRATEGY_OPTIONS",
                    "infrastructure": {
                        "name": "Bridge",
                        "description": "Boosts capacity",
                        "category": "Infrastructure",
                    },
                    "riskControl": None,
                    "finTech": {
                        "name": "Flash",
                        "description": "Extra order quota",
                        "category": "FinTech",
                    },
                }
            ),
            json.dumps(
                {
                    "messageType": "TRADE_NOTIFICATION",
                    "tradeId": 11,
                    "orderId": 88,
                    "price": 1030,
                    "quantity": 4,
                    "side": "Sell",
                    "fee": 3,
                }
            ),
            json.dumps(
                {
                    "messageType": "SKILL_EFFECT",
                    "skillName": "Hedge",
                    "sourcePlayerId": 0,
                    "targetPlayerId": 1,
                    "description": "Protected against one loss",
                }
            ),
            json.dumps(
                {
                    "messageType": "ERROR",
                    "errorCode": 404,
                    "message": "boom",
                }
            ),
            json.dumps(
                {
                    "messageType": "ERROR",
                    "errorCode": "bad",
                    "message": "boom",
                }
            ),
        ]
        fake_ws = FakeWebSocket(messages)
        agent = RecordingAgent()

        with patch("sdk_python.agent.connect", new=AsyncMock(return_value=fake_ws)):
            await agent.run()

        self.assertTrue(fake_ws.closed)
        self.assertEqual(
            [
                {
                    "messageType": "HELLO",
                    "token": "player-1",
                    "role": "player",
                }
            ],
            [json.loads(payload) for payload in fake_ws.sent_messages],
        )
        self.assertEqual(
            [
                ("game", 4),
                ("market", 77),
                ("player", 1200),
                ("news", 9),
                ("report", 240),
                ("strategy", "Bridge"),
                ("trade", 11),
                ("skill", "Hedge"),
                ("error", 404, "boom"),
            ],
            agent.events,
        )

    async def test_run_handles_connect_failure(self) -> None:
        """Connection errors should be swallowed and still trigger cleanup."""

        agent = RecordingAgent()

        async def fake_connect(self) -> None:
            self._ws = None

        with patch.object(Agent, "connect", fake_connect):
            await agent.run()

        self.assertEqual([], agent.events)

    async def test_action_methods_write_expected_payloads(self) -> None:
        """Remaining action methods should serialize their payloads."""

        agent = Agent("player-1")
        fake_ws = FakeWebSocket([])
        agent._ws = fake_ws  # pylint: disable=protected-access

        await agent.limit_sell(1040, 2)
        await agent.cancel_order(7)
        await agent.select_strategy("Bridge")

        self.assertEqual(
            [
                {
                    "messageType": "LIMIT_SELL",
                    "token": "player-1",
                    "price": 1040,
                    "quantity": 2,
                },
                {
                    "messageType": "CANCEL_ORDER",
                    "token": "player-1",
                    "orderId": 7,
                },
                {
                    "messageType": "SELECT_STRATEGY",
                    "token": "player-1",
                    "cardName": "Bridge",
                },
            ],
            [json.loads(payload) for payload in fake_ws.sent_messages],
        )

    def _make_agent(self):
        return CapturingAgent()

    async def test_limit_buy_sends_documented_payload(self) -> None:
        """Limit buys should serialize to the documented wire payload."""

        agent = self._make_agent()

        await agent.limit_buy(1050, 3)

        self.assertEqual(
            [
                {
                    "messageType": "LIMIT_BUY",
                    "token": "player-1",
                    "price": 1050,
                    "quantity": 3,
                }
            ],
            agent.sent_messages,
        )

    async def test_submit_report_serializes_prediction_value(self) -> None:
        """Report submissions should serialize the enum's wire value."""

        agent = self._make_agent()

        await agent.submit_report(7, Prediction.SHORT)

        self.assertEqual(
            [
                {
                    "messageType": "SUBMIT_REPORT",
                    "token": "player-1",
                    "newsId": 7,
                    "prediction": "Short",
                }
            ],
            agent.sent_messages,
        )

    async def test_activate_skill_omits_empty_optional_fields(self) -> None:
        """Skill payloads should omit unset optional fields."""

        agent = self._make_agent()

        await agent.activate_skill("MarketRadar")

        self.assertEqual(
            [
                {
                    "messageType": "ACTIVATE_SKILL",
                    "token": "player-1",
                    "skillName": "MarketRadar",
                }
            ],
            agent.sent_messages,
        )

    async def test_activate_skill_includes_target_and_variant(self) -> None:
        """Skill payloads should include populated optional fields."""

        agent = self._make_agent()

        await agent.activate_skill(
            "Freeze",
            target_player_id=1,
            variant="intense",
        )

        self.assertEqual(
            [
                {
                    "messageType": "ACTIVATE_SKILL",
                    "token": "player-1",
                    "skillName": "Freeze",
                    "targetPlayerId": 1,
                    "variant": "intense",
                }
            ],
            agent.sent_messages,
        )


if __name__ == "__main__":
    unittest.main()
