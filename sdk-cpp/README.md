# THUAI-9 C++ SDK

C++ SDK for building AI agents that connect to the THUAI-9 game server (璃月黄金交易所 — gold trading exchange).

---

## Quick Start

### Build (XMake required)

```bash
cd sdk-cpp
xmake
```

The binary is built to `bin/agent`.

### Run

```bash
TOKEN=player1 SERVER=ws://localhost:14514 ./bin/agent
```

Set `SPDLOG_LEVEL=debug` to see raw inbound/outbound payloads and other low-level transport logs.

If `TOKEN` or `SERVER` is unset, pass `--token` and `--server` on the command line.

### Docker

```bash
docker build -t thuai9-agent .
docker run -e TOKEN=player1 -e SERVER=ws://server:14514 thuai9-agent
```

---

## Building Your Agent

Edit [`src/logic.cpp`](./src/logic.cpp). The bootstrap in `src/main.cpp` stays fixed, so competitors only need to work in one file.

```cpp
#include "logic.hpp"

#include <memory>

#include <spdlog/spdlog.h>

namespace {

class MyAgent final : public thuai::Agent {
public:
    using Agent::Agent;

    void onMarketState(const thuai::MarketState& state) override {
        if (!state.asks.empty() && playerState.mora >= state.asks.front().price) {
            spdlog::info("Buying one unit at {}", state.asks.front().price);
            limitBuy(state.asks.front().price, 1);
        }
    }
};

} // namespace

auto createAgent(std::string token, std::string serverUrl) -> std::unique_ptr<thuai::Agent> {
    return std::make_unique<MyAgent>(std::move(token), std::move(serverUrl));
}
```

---

## API Reference

### `thuai::Agent`

Header-only base class. Connects via WebSocket, dispatches events, keeps the latest snapshots, and emits structured logs with `spdlog`.

#### Auto-Tracked State

| Member            | Type                             | Description                     |
| ----------------- | -------------------------------- | ------------------------------- |
| `gameState`       | `GameState`                      | Stage, day, tick, scores        |
| `marketState`     | `MarketState`                    | Order book + prices             |
| `playerState`     | `PlayerState`                    | Your assets, NAV, orders, cards |
| `latestNews`      | `std::optional<News>`            | Last received news              |
| `strategyOptions` | `std::optional<StrategyOptions>` | Available cards during draft    |

#### Action Methods

```cpp
void limitBuy(long price, int quantity);
void limitSell(long price, int quantity);
void cancelOrder(long orderId);
void submitReport(int newsId, Prediction prediction);
void selectStrategy(const std::string& cardName);
void activateSkill(const std::string& skillName, int targetPlayerId = -1, const std::string& variant = "");
```

#### Event Handlers (virtual, override what you need)

```cpp
virtual void onGameState(const GameState&);
virtual void onMarketState(const MarketState&);
virtual void onPlayerState(const PlayerState&);
virtual void onNews(const News&);
virtual void onReportResult(const ReportResult&);
virtual void onStrategyOptions(const StrategyOptions&);
virtual void onTrade(const TradeNotification&);
virtual void onSkillEffect(const SkillEffect&);
virtual void onError(int code, const std::string& message);
```

All callbacks default to no-op implementations, so your strategy only needs to override the events it actually uses.

#### Run

```cpp
agent.run();  // connect + blocking event loop until game ends
```

---

## Data Structures

All structs are POD-style with default values. See `src/models.hpp`.

### `PlayerState` (private to you)

```cpp
struct PlayerState {
    long mora = 0;          // available
    long frozenMora = 0;    // locked in buy orders
    int gold = 0;           // available
    int frozenGold = 0;     // locked in sell orders
    int lockedGold = 0;     // from 定向增发, locked 300 ticks
    long nav = 0;           // net asset value
    std::vector<std::string> activeCards;
    std::vector<OrderInfo> pendingOrders;
};
```

### `MarketState`

```cpp
struct MarketState {
    std::vector<PriceLevel> bids;  // descending by price
    std::vector<PriceLevel> asks;  // ascending by price
    long lastPrice = 0;
    long midPrice = 0;
    int volume = 0;
    int tick = 0;
};
```

### `Prediction` (enum)

```cpp
enum class Prediction { Long, Short, Hold };
```

---

## Strategy Cards

6 张策略卡，分 3 类：

| 卡名     | 类别       | 参数                      | 说明                                   |
| -------- | ---------- | ------------------------- | -------------------------------------- |
| 内幕消息 | 基建       | `variant: "cheap"` (可选)  | 提前获取新闻预览                       |
| 闪电交易 | 基建       | —                         | 接下来3天每天多一次即时交易            |
| 止损名刀 | 风控       | —                         | 撤销所有挂单，下跌保护                 |
| 定向增发 | 风控       | —                         | 折扣购入锁定黄金                       |
| 网络风暴 | 金融科技   | `targetPlayerId`          | 指定对手，其下一次订单延迟1天           |
| 舆情打击 | 金融科技   | —                         | 伪造新闻广播，干扰对手                 |

使用 `getAllPlayerIds()` 获取所有选手 ID。

---

## Dependencies

- **C++23**
- **ixwebsocket** — WebSocket client
- **nlohmann/json** — JSON parsing
- **spdlog** — Structured logging

Both are pulled in automatically by XMake.

---

## Layout

```plain
sdk-cpp/
├── src/
│   ├── main.cpp        # Bootstrap + logging config
│   ├── logic.cpp       # Your strategy goes here
│   ├── logic.hpp       # Strategy factory declaration
│   ├── agent.hpp       # Agent base class (header-only)
│   └── models.hpp      # Data structures
├── xmake.lua
└── Dockerfile
```

GPLv3
