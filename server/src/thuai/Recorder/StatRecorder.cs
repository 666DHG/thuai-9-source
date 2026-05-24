namespace Thuai.Recorder;

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Thuai.GameLogic;
using Serilog;

public class StatRecorder : IDisposable
{
    private readonly bool _enabled;
    private readonly RecordPage _recordPage = new();
    private readonly string _recordsDir;
    private readonly string _targetFilePath;
    private bool _disposed;

    private int? _currentMonth;
    private GameStage? _previousStage;
    private string[]? _lastDraftOfferings;
    private Dictionary<string, HashSet<string>> _preDraftCardNames = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public StatRecorder(string recordsDir = "./data", bool enabled = true, int flushEveryRecords = 500)
    {
        _recordsDir = recordsDir;
        _enabled = enabled;
        _targetFilePath = Path.Combine(recordsDir, "stat.dat");

        Directory.CreateDirectory(recordsDir);

        if (File.Exists(_targetFilePath))
            File.Delete(_targetFilePath);
    }

    public void Record(object statEvent)
    {
        if (!_enabled) return;

        string json = JsonSerializer.Serialize(statEvent, JsonOptions);
        _recordPage.Enqueue(json);
    }

    public void RecordFromGame(Game game)
    {
        if (!_enabled) return;

        var currentStage = game.Stage;
        var currentMonth = game.CurrentMonthNumber;

        // Month change: flush previous month's events as a single page
        if (_currentMonth.HasValue && currentMonth != _currentMonth.Value)
        {
            FlushMonth(_currentMonth.Value);
        }
        _currentMonth = currentMonth;

        // Entering StrategySelection: capture draft offerings and pre-draft card snapshots
        if (currentStage == GameStage.StrategySelection && _previousStage != GameStage.StrategySelection)
        {
            var mgr = game.CardManager;
            var offerings = new List<string>(3);
            if (mgr.CurrentInfrastructure != null) offerings.Add(mgr.CurrentInfrastructure.Name);
            if (mgr.CurrentRiskControl != null) offerings.Add(mgr.CurrentRiskControl.Name);
            if (mgr.CurrentFinTech != null) offerings.Add(mgr.CurrentFinTech.Name);
            _lastDraftOfferings = offerings.ToArray();

            _preDraftCardNames = game.Players.Values.ToDictionary(
                p => p.Token,
                p => p.ActiveCards.Select(c => c.Name).ToHashSet()
            );
        }

        // Leaving StrategySelection into TradingDay: record selections
        if (_previousStage == GameStage.StrategySelection && currentStage == GameStage.TradingDay && _lastDraftOfferings != null)
        {
            var selections = new Dictionary<string, string>();
            foreach (var player in game.Players.Values)
            {
                var preNames = _preDraftCardNames.GetValueOrDefault(player.Token, []);
                var newCard = player.ActiveCards.FirstOrDefault(c => !preNames.Contains(c.Name));
                if (newCard != null)
                {
                    selections[player.Token] = newCard.Name;
                }
            }

            Record(new
            {
                type = "draft",
                month = game.CurrentMonthNumber,
                offerings = _lastDraftOfferings,
                selections
            });
        }

        // TradingDay events: news, reports, skills
        if (currentStage == GameStage.TradingDay && game.CurrentTradingDay != null)
        {
            var td = game.CurrentTradingDay;
            int month = game.CurrentMonthNumber;

            foreach (var news in td.PublishedNewsThisDay)
            {
                Record(new
                {
                    type = "news",
                    month,
                    newsId = news.NewsId,
                    publishTick = news.PublishTick,
                    content = news.Content,
                    sentiment = news.Sentiment.ToString(),
                    isFake = news.IsFake,
                    sourcePlayer = news.SourcePlayer
                });
            }

            foreach (var report in td.SettledReportsThisDay)
            {
                Record(new
                {
                    type = "report",
                    playerToken = report.PlayerToken,
                    newsId = report.NewsId,
                    prediction = report.Prediction.ToString(),
                    submitTick = report.SubmitTick,
                    settlementTick = report.SettlementDay,
                    submissionRank = report.SubmissionRank,
                    isCorrect = report.IsCorrect,
                    actualChange = report.ActualChange,
                    reward = report.Reward
                });
            }

            foreach (var skill in td.SkillEffectsThisDay)
            {
                Record(new
                {
                    type = "skill",
                    month,
                    tick = game.CurrentTick,
                    sourcePlayer = skill.SourcePlayer,
                    skillName = skill.SkillName,
                    description = skill.Description,
                    targetPlayer = skill.TargetPlayer
                });
            }

            foreach (var trade in td.TradesThisDay)
            {
                Record(new
                {
                    type = "trade",
                    month,
                    tick = trade.Tick,
                    tradeId = trade.TradeId,
                    buyOrderId = trade.BuyOrderId,
                    sellOrderId = trade.SellOrderId,
                    buyerToken = trade.BuyerToken,
                    sellerToken = trade.SellerToken,
                    price = trade.Price,
                    quantity = trade.Quantity,
                    buyerFee = trade.BuyerFee,
                    sellerFee = trade.SellerFee
                });
            }
        }

        _previousStage = currentStage;
    }

    public void Save()
    {
        // StatRecorder flushes at month boundaries; periodic saves are not needed.
    }

    public void Flush()
    {
        if (!_enabled) return;
        if (_recordPage.Length > 0 && _currentMonth.HasValue)
            FlushMonth(_currentMonth.Value);
    }

    private void FlushMonth(int month)
    {
        if (_recordPage.Length == 0) return;

        try
        {
            string pageName = $"{month}.json";
            string content = _recordPage.ToJson();

            using var zipFile = new FileStream(_targetFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            using var archive = new ZipArchive(zipFile, ZipArchiveMode.Update);

            var existing = archive.GetEntry(pageName);
            existing?.Delete();

            var entry = archive.CreateEntry(pageName, CompressionLevel.SmallestSize);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);

            _recordPage.Clear();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save stat recording for month {Month}", month);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Flush();
    }
}
