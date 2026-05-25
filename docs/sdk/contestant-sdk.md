# 选手 SDK 调用说明

本仓库提供 Python 与 C++ 两套选手 SDK。两者都通过 WebSocket 连接服务端，接收状态回调，并在回调中发送交易、研报、策略和技能动作。

## 快速开始

默认服务端地址：

```text
ws://localhost:14514
```

服务端通过 `TOKENS` 环境变量加载选手 token，默认逗号分隔：

```bash
TOKENS=player1,player2 dotnet run --project server/src/thuai
```

选手进程通过环境变量指定自己的身份和服务端地址：

```bash
TOKEN=player1 SERVER=ws://localhost:14514 <run-agent-command>
```

如果未设置，示例代码默认使用：

- `TOKEN=player1`。
- `SERVER=ws://localhost:14514`。

## 通用生命周期

1. 创建 Agent，并传入 token 和 server 地址。
2. 连接 WebSocket。
3. SDK 持续接收消息并更新本地状态。
4. SDK 根据 `messageType` 调用对应回调。
5. 选手在回调中调用动作函数。
6. 比赛结束或连接关闭后退出。

当前 SDK 兼容服务端的旧式懒绑定：第一次发送带 `token` 的动作消息时，服务端会把该 socket 绑定到对应玩家。协议升级后，SDK 可增加 `HELLO` 显式握手；动作消息仍建议保留 token。

## Python SDK

入口文件：

- `sdk-python/main.py`。
- `sdk-python/sdk_python/agent.py`。
- `sdk-python/sdk_python/models.py`。

### 安装依赖

Python SDK 需要 Python 3.11+ 和 `websockets`。

```bash
cd sdk-python
python3 -m pip install -e .
```

### 运行示例

```bash
cd sdk-python
TOKEN=player1 SERVER=ws://localhost:14514 python3 main.py
```

### 基本写法

继承 `Agent`，覆写 async 回调，最后调用 `await agent.run()`。

```python
import asyncio
import os

from sdk_python.agent import Agent
from sdk_python.models import MarketState, News, Prediction


class MyAgent(Agent):
    async def on_market_state(self, state: MarketState):
        if state.mid_price > 0:
            await self.limit_buy(state.mid_price - 1, 1)

    async def on_news(self, news: News):
        await self.submit_report(news.news_id, Prediction.LONG)


async def main():
    agent = MyAgent(
        token=os.environ.get("TOKEN", "player1"),
        server_url=os.environ.get("SERVER", "ws://localhost:14514"),
    )
    await agent.run()


asyncio.run(main())
```

### Python 回调

可覆写：

- `on_game_state(state: GameState)`。
- `on_market_state(state: MarketState)`。
- `on_player_state(state: PlayerState)`。
- `on_news(news: News)`。
- `on_report_result(result: ReportResult)`。
- `on_strategy_options(options: StrategyOptions)`。
- `on_trade(trade: TradeNotification)`。
- `on_skill_effect(effect: SkillEffect)`。
- `on_error(code: int, message: str)`。

SDK 会自动更新：

- `self.game_state`。
- `self.market_state`。
- `self.player_state`。
- `self.latest_news`。
- `self.strategy_options`。

### Python 动作函数

```python
await self.limit_buy(price, quantity)
await self.limit_sell(price, quantity)
await self.cancel_order(order_id)
await self.submit_report(news_id, Prediction.LONG)
await self.select_strategy(card_name)
await self.activate_skill(skill_name, direction="buy")
```

`Prediction` 取值：

- `Prediction.LONG` -> `Long`。
- `Prediction.SHORT` -> `Short`。
- `Prediction.HOLD` -> `Hold`。

回调函数运行在消息处理循环中。接入大模型或外部服务时，使用 async 调用并设置超时，避免阻塞后续行情。

## C++ SDK

入口文件：

- `sdk-cpp/src/main.cpp`。
- `sdk-cpp/src/agent.hpp`。
- `sdk-cpp/src/models.hpp`。

依赖：

- `ixwebsocket`。
- `nlohmann_json`。
- C++17。

### 构建与运行

```bash
cd sdk-cpp
xmake
TOKEN=player1 SERVER=ws://localhost:14514 xmake run agent
```

### 基本写法

继承 `thuai::Agent`，覆写 virtual 回调，最后调用 `agent.run()`。

```cpp
#include "agent.hpp"
#include <cstdlib>

class MyAgent : public thuai::Agent {
public:
    using Agent::Agent;

    void onMarketState(const thuai::MarketState& state) override {
        if (state.midPrice > 0) {
            limitBuy(state.midPrice - 1, 1);
        }
    }

    void onNews(const thuai::News& news) override {
        submitReport(news.newsId, thuai::Prediction::Long);
    }
};

int main() {
    const char* token = std::getenv("TOKEN");
    const char* server = std::getenv("SERVER");

    MyAgent agent(
        token ? token : "player1",
        server ? server : "ws://localhost:14514"
    );
    agent.run();
}
```

### C++ 回调

可覆写：

- `onGameState(const GameState&)`。
- `onMarketState(const MarketState&)`。
- `onPlayerState(const PlayerState&)`。
- `onNews(const News&)`。
- `onReportResult(const ReportResult&)`。
- `onStrategyOptions(const StrategyOptions&)`。
- `onTrade(const TradeNotification&)`。
- `onSkillEffect(const SkillEffect&)`。
- `onError(int code, const std::string& message)`。

SDK 会自动更新：

- `gameState`。
- `marketState`。
- `playerState`。
- `latestNews`。
- `strategyOptions`。

### C++ 动作函数

```cpp
limitBuy(price, quantity);
limitSell(price, quantity);
cancelOrder(orderId);
submitReport(newsId, thuai::Prediction::Long);
selectStrategy(cardName);
activateSkill(skillName, "buy");
```

`Prediction` 取值：

- `thuai::Prediction::Long` -> `Long`。
- `thuai::Prediction::Short` -> `Short`。
- `thuai::Prediction::Hold` -> `Hold`。

## 状态字段语义

### GameState

- `stage`：当前阶段，取 `Waiting`、`PreparingGame`、`StrategySelection`、`TradingDay`、`Settlement`、`Finished`。
- `currentDay` / `current_day`：当前交易日，1 起始。
- `currentTick` / `current_tick`：整场比赛全局 Tick。
- `totalTicks` / `total_ticks`：当前实现中 TradingDay 阶段为日内 Tick，非 TradingDay 为 0。
- `scores`：比分列表。

### MarketState

- `bids`：买盘可见档位。
- `asks`：卖盘可见档位。
- `lastPrice` / `last_price`：最新成交价。
- `midPrice` / `mid_price`：盘口中间价。
- `volume`：当日累计成交量。
- `tick`：交易日内 Tick。

不要把 `volume` 当作单 Tick 成交量。如果需要单 Tick 或 K 线成交量，需要自行保存上一条 `volume` 并做差分。

### PlayerState

- `mora`：可用摩拉。
- `frozenMora` / `frozen_mora`：买单冻结摩拉。
- `gold`：可用黄金。
- `frozenGold` / `frozen_gold`：卖单冻结黄金。
- `lockedGold` / `locked_gold`：策略造成的锁定黄金。
- `nav`：按当前中间价估算的净值。
- `activeCards` / `active_cards`：已生效策略卡名称。
- `pendingOrders` / `pending_orders`：当前未完成挂单。

## 规则限制

- 每场 3 个月，每月 30 天。
- 每月初始资产为 1,000,000 摩拉与 1,000 单位黄金。
- 默认网络延迟为 1 天（指令次日生效）。
- 默认手续费率为 0.02%。
- 每天每位选手最多 1 次即时交易 + 1 次挂单。
- 撤单不计入上限。
- 新闻发布后 2 天内可提交研报。
- 新闻发布后第 3 天结算研报。
- 策略选择阶段 40 Tick。
- 禁止杠杆与裸做空（除非策略卡触发）。

## 策略与技能

策略卡共 6 张，分三类（每类 2 张）：

- 基建：内幕消息、闪电交易。
- 风控：止损名刀、定向增发。
- 金融科技：网络风暴、舆情打击。

选择策略：

```python
await self.select_strategy("内幕消息")
```

```cpp
selectStrategy("内幕消息");
```

激活技能：

```python
await self.activate_skill("网络风暴", target_player_id=1)
```

```cpp
activateSkill("网络风暴", 1);
```

`target_player_id` 当前主要用于“网络风暴”，指定目标选手的 PlayerID。通过 `get_all_player_ids()` / `getAllPlayerIds()` 获取所有在线选手的 ID 列表。

获取所有选手 ID：

```python
ids = self.get_all_player_ids()  # 从 GameState.scores 提取
```

```cpp
auto ids = getAllPlayerIds();  // 从 gameState.scores 提取
```

## 常见陷阱

- 不要在行情回调里长时间同步阻塞。
- 不要把 `market_state.volume` 当作单 Tick 成交量。
- 下单前检查 `stage == "TradingDay"`。
- 策略选择只在 `StrategySelection` 阶段有效。
- 研报必须在新闻发布后 2 天内提交。
- 限价卖出需要持有可用黄金，除非规则或策略明确允许。
- 订单经过默认 1 天网络延迟后才进入撮合。
- 主动技能有使用次数/CD限制，调用失败时关注 `on_error`。
- 优先发送 `HELLO` 握手，通过 `PlayerState.playerId` 获取自己的 ID。

## 调试建议

- 打印 `GAME_STATE.stage` 和 `MARKET_STATE.tick`，确认策略在正确阶段执行。
- 对每个新闻保存 `newsId`、`publishTick` 和提交时 Tick，避免重复提交。
- 对每笔订单保存本地意图和服务端 `pendingOrders` 状态，撤单前确认订单仍未完成。
- 为交易逻辑设置最小价差、最大仓位和最大挂单数量，避免回调内重复下单。
