# THUAI-9 Python SDK

Python SDK for building AI agents that connect to the THUAI-9 game server (璃月黄金交易所 — gold trading exchange).

---

## Quick Start

### Install

```bash
# Using PDM
pdm install

# Or pip
pip install websockets
```

### Run the example agent

```bash
python main.py --token player1 --server ws://localhost:14514
```

You can still use environment variables if you prefer:

```bash
TOKEN=player1 SERVER=ws://localhost:14514 python main.py
```

---

## Building Your Agent

Subclass `Agent` and override the event handlers you care about:

```python
import asyncio
import os
from sdk_python.agent import Agent
from sdk_python.models import GameState, MarketState, PlayerState, News, StrategyOptions, Prediction

class MyAgent(Agent):
    async def on_market_state(self, state: MarketState):
        if state.bids and state.asks:
            spread = state.asks[0].price - state.bids[0].price
            if spread > 5 and self.player_state.gold > 0:
                await self.limit_sell(state.asks[0].price, 1)

    async def on_news(self, news: News):
        await self.submit_report(news.news_id, Prediction.LONG)

    async def on_strategy_options(self, options: StrategyOptions):
        if options.fin_tech:
            await self.select_strategy(options.fin_tech.name)
        elif options.infrastructure:
            await self.select_strategy(options.infrastructure.name)

async def main():
    agent = MyAgent(
        token=os.environ.get("TOKEN", "player1"),
        server_url=os.environ.get("SERVER", "ws://localhost:14514"),
    )
    await agent.run()

if __name__ == "__main__":
    asyncio.run(main())
```

---

## API Reference

### `Agent(token, server_url)`

Base class. Connects to the server, receives messages, dispatches events.

#### Auto-Tracked State

| Property                 | Type                      | Description                                    |
| ------------------------ | ------------------------- | ---------------------------------------------- |
| `agent.game_state`       | `GameState`               | Stage, day, tick, scoreboard                   |
| `agent.market_state`     | `MarketState`             | Order book + prices                            |
| `agent.player_state`     | `PlayerState`             | Your assets, NAV, pending orders, active cards |
| `agent.player_state.player_id` | `int`                | 本选手的 PlayerID                              |
| `agent.latest_news`      | `News \| None`            | Last received news                             |
| `agent.strategy_options` | `StrategyOptions \| None` | Cards available during draft                   |

#### Action Methods

```python
await agent.limit_buy(price: int, quantity: int)
await agent.limit_sell(price: int, quantity: int)
await agent.cancel_order(order_id: int)
await agent.submit_report(news_id: int, prediction: Prediction)
await agent.select_strategy(card_name: str)
await agent.activate_skill(skill_name: str, target_player_id: int | None = None, variant: str | None = None)
```

#### Event Handlers (override these)

```python
async def on_game_state(self, state: GameState): ...
async def on_market_state(self, state: MarketState): ...
async def on_player_state(self, state: PlayerState): ...
async def on_news(self, news: News): ...
async def on_report_result(self, result: ReportResult): ...
async def on_strategy_options(self, options: StrategyOptions): ...
async def on_trade(self, trade: TradeNotification): ...
async def on_skill_effect(self, effect: SkillEffect): ...
async def on_error(self, code: int, message: str): ...
```

#### Run

```python
await agent.run()  # connect + event loop, blocks until disconnected
```

---

## Data Models

All models are dataclasses. Wire format uses camelCase, Python uses snake_case (auto-converted).

### `PlayerState` (private to you)

```python
mora: int               # available Mora
frozen_mora: int        # locked in pending buy orders
gold: int               # available gold
frozen_gold: int        # locked in pending sell orders
locked_gold: int        # gold from 定向增发, unlocks after 300 ticks
nav: int                # net asset value (mora + frozen_mora + (gold + ...) * mid_price)
active_cards: list[str]
pending_orders: list[OrderInfo]
```

### `MarketState`

```python
bids: list[PriceLevel]  # descending by price
asks: list[PriceLevel]  # ascending by price
last_price: int
mid_price: int
volume: int
tick: int
```

### `Prediction` (enum)

```python
Prediction.LONG     # predict price will go up
Prediction.SHORT    # predict price will go down
Prediction.HOLD     # no prediction (safe)
```

See `sdk_python/models.py` for all types.

---

## Strategy Cards

6 张策略卡，分 3 类（每类 2 张）：

| 卡名     | 类别       | 参数                     | 说明                                   |
| -------- | ---------- | ------------------------ | -------------------------------------- |
| 内幕消息 | 基建       | `variant: "cheap"` (可选) | 提前获取新闻预览                       |
| 闪电交易 | 基建       | —                        | 接下来3天每天多一次即时交易            |
| 止损名刀 | 风控       | —                        | 撤销所有挂单，下跌保护                 |
| 定向增发 | 风控       | —                        | 折扣购入锁定黄金                       |
| 网络风暴 | 金融科技   | `target_player_id`       | 指定对手，其下一次订单延迟1天           |
| 舆情打击 | 金融科技   | —                        | 伪造新闻广播，干扰对手                 |

使用 `get_all_player_ids()` 获取所有选手 ID，用于 `网络风暴` 等需指定目标的技能。

---

## Layout

```plain
sdk-python/
├── sdk_python/
│   ├── __init__.py
│   ├── agent.py
│   └── models.py
├── main.py
├── pyproject.toml
└── Dockerfile
```

GPLv3
