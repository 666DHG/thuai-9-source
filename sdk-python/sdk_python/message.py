"""Wire-format message parsing helpers for the THUAI websocket SDK."""

from __future__ import annotations

from typing import List, Dict, Optional, TypeAlias, cast

from .models import (
    CardOption,
    GameState,
    MarketState,
    News,
    OrderInfo,
    PlayerScore,
    PlayerState,
    PriceLevel,
    ReportResult,
    SkillEffect,
    StrategyOptions,
    TradeNotification,
)

JsonObject: TypeAlias = Dict[str, object]


def _require_int(data: JsonObject, key: str) -> int:
    """Read a required integer field and validate its type."""

    value = data[key]
    if not isinstance(value, int) or isinstance(value, bool):
        raise TypeError(f"Field '{key}' must be int, got {type(value).__name__}")
    return value


def _require_bool(data: JsonObject, key: str) -> bool:
    """Read a required boolean field and validate its type."""

    value = data[key]
    if not isinstance(value, bool):
        raise TypeError(f"Field '{key}' must be bool, got {type(value).__name__}")
    return value


def _require_str(data: JsonObject, key: str) -> str:
    """Read a required string field and validate its type."""

    value = data[key]
    if not isinstance(value, str):
        raise TypeError(f"Field '{key}' must be str, got {type(value).__name__}")
    return value


def _require_list(data: JsonObject, key: str) -> List[object]:
    """Read a required list field and validate its type."""

    value = data[key]
    if not isinstance(value, list):
        raise TypeError(f"Field '{key}' must be list, got {type(value).__name__}")
    return value  # type: ignore


def _optional_str(data: JsonObject, key: str) -> Optional[str]:
    """Read an optional string field and validate its type if present."""

    value = data[key]
    if value is None:
        return None
    if not isinstance(value, str):
        raise TypeError(
            f"Field '{key}' must be str or null, got {type(value).__name__}"
        )
    return value


def _optional_object(data: JsonObject, key: str) -> Optional[JsonObject]:
    """Read an optional object field and validate its type if present."""

    value = data[key]
    if value is None:
        return None
    if not isinstance(value, dict):
        raise TypeError(
            f"Field '{key}' must be object or null, got {type(value).__name__}"
        )
    return cast(JsonObject, value)


def _require_str_list(data: JsonObject, key: str) -> list[str]:
    """Read a required string list field and validate all entries."""

    values = _require_list(data, key)
    result: list[str] = []
    for index, value in enumerate(values):
        if not isinstance(value, str):
            raise TypeError(
                f"Field '{key}[{index}]' must be str, got {type(value).__name__}"
            )
        result.append(value)
    return result


def _require_object_list(data: JsonObject, key: str) -> list[JsonObject]:
    """Read a required object list field and validate all entries."""

    values = _require_list(data, key)
    result: list[JsonObject] = []
    for index, value in enumerate(values):
        if not isinstance(value, dict):
            raise TypeError(
                f"Field '{key}[{index}]' must be object, got {type(value).__name__}"
            )
        result.append(cast(JsonObject, value))
    return result


def parse_inbound_message(data: object) -> tuple[str, JsonObject]:
    """Validate an inbound message envelope and return its type and payload."""

    if not isinstance(data, dict):
        raise TypeError("Inbound message must be a JSON object")
    payload = cast(JsonObject, data)
    return _require_str(payload, "messageType"), payload


def parse_game_state(data: JsonObject) -> GameState:
    """Convert a wire-format game-state payload into a SDK model."""

    scores = [
        PlayerScore(_require_int(score, "playerId"), _require_int(score, "score"))
        for score in _require_object_list(data, "scores")
    ]
    return GameState(
        stage=_require_str(data, "stage"),
        current_month=_require_int(data, "currentMonth"),
        current_day=_require_int(data, "currentDay"),
        current_tick=_require_int(data, "currentTick"),
        total_ticks=_require_int(data, "totalTicks"),
        scores=scores,
    )


def parse_market_state(data: JsonObject) -> MarketState:
    """Convert a wire-format market-state payload into a SDK model."""

    bids = [
        PriceLevel(_require_int(level, "price"), _require_int(level, "quantity"))
        for level in _require_object_list(data, "bids")
    ]
    asks = [
        PriceLevel(_require_int(level, "price"), _require_int(level, "quantity"))
        for level in _require_object_list(data, "asks")
    ]
    return MarketState(
        bids=bids,
        asks=asks,
        last_price=_require_int(data, "lastPrice"),
        mid_price=_require_int(data, "midPrice"),
        volume=_require_int(data, "volume"),
        tick=_require_int(data, "tick"),
    )


def parse_player_state(data: JsonObject) -> PlayerState:
    """Convert a wire-format player-state payload into a SDK model."""

    orders = [
        OrderInfo(
            _require_int(order, "orderId"),
            _require_int(order, "arrivalTick"),
            _require_str(order, "side"),
            _require_int(order, "price"),
            _require_int(order, "quantity"),
            _require_int(order, "remainingQuantity"),
            _require_str(order, "status"),
            _require_str(order, "intent"),
        )
        for order in _require_object_list(data, "pendingOrders")
    ]
    return PlayerState(
        player_id=_require_int(data, "playerId"),
        mora=_require_int(data, "mora"),
        frozen_mora=_require_int(data, "frozenMora"),
        gold=_require_int(data, "gold"),
        frozen_gold=_require_int(data, "frozenGold"),
        locked_gold=_require_int(data, "lockedGold"),
        nav=_require_int(data, "nav"),
        network_delay=_require_int(data, "networkDelay"),
        immediate_orders_used_today=_require_int(data, "immediateOrdersUsedToday"),
        resting_orders_used_today=_require_int(data, "restingOrdersUsedToday"),
        bonus_immediate_orders_today=_require_int(data, "bonusImmediateOrdersToday"),
        monthly_trade_count=_require_int(data, "monthlyTradeCount"),
        active_cards=_require_str_list(data, "activeCards"),
        pending_orders=orders,
    )


def parse_news(data: JsonObject) -> News:
    """Convert a wire-format news payload into a SDK model."""

    return News(
        _require_int(data, "month"),
        _require_int(data, "day"),
        _require_int(data, "newsId"),
        _require_str(data, "content"),
        _require_int(data, "publishTick"),
    )


def parse_report_result(data: JsonObject) -> ReportResult:
    """Convert a wire-format report-result payload into a SDK model."""

    return ReportResult(
        _require_int(data, "newsId"),
        _require_int(data, "submissionRank"),
        _require_int(data, "submitTick"),
        _require_int(data, "settlementTick"),
        _require_str(data, "prediction"),
        _require_bool(data, "isCorrect"),
        _require_int(data, "reward"),
        _require_int(data, "actualChange"),
    )


def parse_strategy_options(data: JsonObject) -> StrategyOptions:
    """Convert a wire-format strategy-options payload into a SDK model."""

    def parse_card(card: Optional[JsonObject]) -> Optional[CardOption]:
        """Parse a single card entry if it is present."""

        if card is None:
            return None
        return CardOption(
            _require_str(card, "name"),
            _require_str(card, "description"),
            _require_str(card, "category"),
        )

    return StrategyOptions(
        parse_card(_optional_object(data, "infrastructure")),
        parse_card(_optional_object(data, "riskControl")),
        parse_card(_optional_object(data, "finTech")),
    )


def parse_trade_notification(data: JsonObject) -> TradeNotification:
    """Convert a wire-format trade-notification payload into a SDK model."""

    return TradeNotification(
        _require_int(data, "tradeId"),
        _require_int(data, "orderId"),
        _require_int(data, "price"),
        _require_int(data, "quantity"),
        _require_str(data, "side"),
        _require_int(data, "fee"),
    )


def _optional_int(data: JsonObject, key: str) -> Optional[int]:
    """Read an optional int field and validate its type if present."""

    value = data.get(key)
    if value is None:
        return None
    if not isinstance(value, int) or isinstance(value, bool):
        raise TypeError(
            f"Field '{key}' must be int or null, got {type(value).__name__}"
        )
    return value


def parse_skill_effect(data: JsonObject) -> SkillEffect:
    """Convert a wire-format skill-effect payload into a SDK model."""

    return SkillEffect(
        _require_str(data, "skillName"),
        _require_int(data, "sourcePlayerId"),
        _optional_int(data, "targetPlayerId"),
        _require_str(data, "description"),
    )


def parse_error_message(data: JsonObject) -> tuple[int, str]:
    """Convert an error payload into code/message fields."""

    return _require_int(data, "errorCode"), _require_str(data, "message")
