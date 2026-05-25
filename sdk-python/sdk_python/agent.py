"""High-level asynchronous client for the THUAI websocket protocol."""

from __future__ import annotations

import json
import logging
from typing import Dict, Optional, TypeAlias

from websockets import connect
from websockets.asyncio.client import ClientConnection

from .message import (
    JsonObject,
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
from .models import (
    GameState,
    MarketState,
    News,
    PlayerState,
    Prediction,
    ReportResult,
    SkillEffect,
    StrategyOptions,
    TradeNotification,
)

OutgoingMessage: TypeAlias = Dict[str, object]

logger = logging.getLogger("thuai")


class Agent:  # pylint: disable=too-many-instance-attributes
    """Stateful websocket agent that sends actions and tracks server snapshots."""

    def __init__(self, token: str, server_url: str = "ws://localhost:14514") -> None:
        self.token = token
        self.server_url = server_url
        self._ws: Optional[ClientConnection] = None

        # Current state is refreshed automatically as snapshots arrive.
        self.game_state = GameState()
        self.market_state = MarketState()
        self.player_state = PlayerState()
        self.latest_news: Optional[News] = None
        self.strategy_options: Optional[StrategyOptions] = None

    async def connect(self) -> None:
        """Open the websocket connection and send HELLO."""

        self._ws = await connect(self.server_url)
        logger.info("Connected to %s", self.server_url)
        await self._send(
            {
                "messageType": "HELLO",
                "token": self.token,
                "role": "player",
            }
        )

    async def disconnect(self) -> None:
        """Close the websocket connection if it is currently open."""

        if self._ws:
            await self._ws.close()

    # --- Actions ---

    async def limit_buy(self, price: int, quantity: int) -> None:
        """Submit a limit buy order."""

        await self._send(
            {
                "messageType": "LIMIT_BUY",
                "token": self.token,
                "price": price,
                "quantity": quantity,
            }
        )

    async def limit_sell(self, price: int, quantity: int) -> None:
        """Submit a limit sell order."""

        await self._send(
            {
                "messageType": "LIMIT_SELL",
                "token": self.token,
                "price": price,
                "quantity": quantity,
            }
        )

    async def cancel_order(self, order_id: int) -> None:
        """Cancel an existing order by identifier."""

        await self._send(
            {
                "messageType": "CANCEL_ORDER",
                "token": self.token,
                "orderId": order_id,
            }
        )

    async def submit_report(self, news_id: int, prediction: Prediction) -> None:
        """Submit a research report for a published news item."""

        await self._send(
            {
                "messageType": "SUBMIT_REPORT",
                "token": self.token,
                "newsId": news_id,
                "prediction": prediction.value,
            }
        )

    async def select_strategy(self, card_name: str) -> None:
        """Choose a strategy card during the strategy-selection stage."""

        await self._send(
            {
                "messageType": "SELECT_STRATEGY",
                "token": self.token,
                "cardName": card_name,
            }
        )

    async def activate_skill(
        self,
        skill_name: str,
        target_player_id: Optional[int] = None,
        variant: Optional[str] = None,
    ) -> None:
        """Activate a skill, optionally targeting a player by ID or variant."""

        msg: OutgoingMessage = {
            "messageType": "ACTIVATE_SKILL",
            "token": self.token,
            "skillName": skill_name,
        }
        if target_player_id is not None:
            msg["targetPlayerId"] = target_player_id
        if variant:
            msg["variant"] = variant
        await self._send(msg)

    def get_all_player_ids(self) -> list[int]:
        """Return the list of all player IDs from the latest game state scores."""

        return [s.player_id for s in self.game_state.scores]

    # --- Event Loop ---

    async def run(self) -> None:
        """Connect, consume server messages, and dispatch callbacks."""

        await self.connect()
        try:
            if self._ws is None:
                raise RuntimeError("Websocket connection is not open")
            async for raw in self._ws:
                try:
                    msg_type, payload = parse_inbound_message(json.loads(raw))
                    self._update_state(msg_type, payload)
                    await self._dispatch(msg_type, payload)
                except json.JSONDecodeError:
                    logger.warning("Invalid JSON: %s", raw)
                except Exception as exc:  # pylint: disable=broad-exception-caught
                    logger.error("Error handling message: %s", exc)
        except Exception as exc:  # pylint: disable=broad-exception-caught
            logger.error("Connection error: %s", exc)
        finally:
            await self.disconnect()

    # --- Override these in your agent ---

    async def on_game_state(self, state: GameState) -> None:
        """Handle a new game-state snapshot."""

    async def on_market_state(self, state: MarketState) -> None:
        """Handle a new market-state snapshot."""

    async def on_player_state(self, state: PlayerState) -> None:
        """Handle a new player-state snapshot."""

    async def on_news(self, news: News) -> None:
        """Handle a newly published news item."""

    async def on_report_result(self, result: ReportResult) -> None:
        """Handle the settlement result of a submitted report."""

    async def on_strategy_options(self, options: StrategyOptions) -> None:
        """Handle the current strategy card choices."""

    async def on_trade(self, trade: TradeNotification) -> None:
        """Handle a trade execution notification."""

    async def on_skill_effect(self, effect: SkillEffect) -> None:
        """Handle a skill effect broadcast."""

    async def on_error(self, code: int, message: str) -> None:
        """Handle an error message from the server."""

    # --- Internal ---

    async def _send(self, data: OutgoingMessage) -> None:
        """Serialize and send a client action payload."""

        if self._ws is not None:
            await self._ws.send(json.dumps(data))

    def _update_state(self, msg_type: str, data: JsonObject) -> None:
        """Refresh cached state from an inbound snapshot message."""

        if msg_type == "GAME_STATE":
            self.game_state = parse_game_state(data)
        elif msg_type == "MARKET_STATE":
            self.market_state = parse_market_state(data)
        elif msg_type == "PLAYER_STATE":
            self.player_state = parse_player_state(data)
        elif msg_type == "NEWS_BROADCAST":
            self.latest_news = parse_news(data)
        elif msg_type == "STRATEGY_OPTIONS":
            self.strategy_options = parse_strategy_options(data)

    async def _dispatch(self, msg_type: str, data: JsonObject) -> None:
        """Invoke the public callback that matches an inbound message type."""

        if msg_type == "GAME_STATE":
            await self.on_game_state(self.game_state)
        elif msg_type == "MARKET_STATE":
            await self.on_market_state(self.market_state)
        elif msg_type == "PLAYER_STATE":
            await self.on_player_state(self.player_state)
        elif msg_type == "NEWS_BROADCAST":
            if self.latest_news is not None:
                await self.on_news(self.latest_news)
        elif msg_type == "REPORT_RESULT":
            await self.on_report_result(parse_report_result(data))
        elif msg_type == "STRATEGY_OPTIONS":
            if self.strategy_options is not None:
                await self.on_strategy_options(self.strategy_options)
        elif msg_type == "TRADE_NOTIFICATION":
            await self.on_trade(parse_trade_notification(data))
        elif msg_type == "SKILL_EFFECT":
            await self.on_skill_effect(parse_skill_effect(data))
        elif msg_type == "ERROR":
            code, message = parse_error_message(data)
            await self.on_error(code, message)
