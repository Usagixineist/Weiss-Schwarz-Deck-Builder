using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Infrastructure;
using WeissDeckBuilder.Models;
using WeissDeckBuilder.Search;
using WeissDeckBuilder.UI;
using WeissSchwarzDeckSync;

internal static class SmokeTests
{
    private static int _passed;
    private static int _failed;
    private static readonly StringBuilder Log = new StringBuilder();

    /// <summary>
    /// One selectable slice of the suite. Groups exist so a new feature is
    /// checked without re-running (and re-reading) everything else:
    /// <c>run-tests.ps1 -Group import</c> only pays for the 导入卡组 checks.
    /// Groups that need the card database share one lazily loaded instance,
    /// and a group that throws fails on its own line instead of aborting the
    /// unrelated groups behind it.
    /// </summary>
    private sealed class TestGroup
    {
        public TestGroup(string key, string label, bool needsDatabase,
            Action<Func<CardDatabase>> run)
        {
            Key = key;
            Label = label;
            NeedsDatabase = needsDatabase;
            Run = run;
        }

        public string Key { get; private set; }
        public string Label { get; private set; }
        public bool NeedsDatabase { get; private set; }
        public Action<Func<CardDatabase>> Run { get; private set; }
    }

    private static List<TestGroup> BuildGroups()
    {
        return new List<TestGroup>
        {
            new TestGroup("metadata", "系列元数据、S18 解析与封面", true,
                db => TestMetadataAndParser(db())),
            new TestGroup("search", "真实数据与合成数据的筛选", true,
                db => { TestRealSearch(db().GetSeries("加速世界")); TestSyntheticSearches(); }),
            new TestGroup("sort", "三种界面排序与特征排序回归", true,
                db => { TestRealSorts(db().GetSeries("加速世界")); TestSyntheticTraitSort(); }),
            new TestGroup("related", "关联卡解析与卡名匹配", true,
                db => { TestRelatedCardResolution(db()); TestCardNameMatcher(); }),
            new TestGroup("deck", "卡组库、组卡与统计", true,
                db => { TestDeckLibrary(db()); TestDeckComposition(db()); TestDeckStatistics(); TestDeckDelete(); }),
            new TestGroup("draft", "暂存与保存", false,
                db => { TestDeckDraft(); TestDeckSaving(); TestDeckDraftStore(); }),
            new TestGroup("import", "导入卡组弹层的删除与 Esc 顺序", false,
                db => TestDeckImportOverlayEscape()),
            new TestGroup("sync", "卡组同步插件", false,
                db => TestDeckSyncPlanner()),
            new TestGroup("cardcode", "卡号与文本助手", false,
                db => TestCardCodeHelpers()),
            new TestGroup("export", "卡组导出：文件名、PNG 与 PDF", false,
                db => TestDeckExport()),
            new TestGroup("cardpack", "卡表压缩包导入（ZIP/RAR/7z）", false,
                db => TestCardPackArchives()),
            new TestGroup("collection", "我的收藏：数量文件与存储", false,
                db => TestCollectionStore())
        };
    }

    private static int Main(string[] args)
    {
        string logPath = ReadLogPath(args);
        List<TestGroup> groups = BuildGroups();

        if (HasFlag(args, "--list"))
        {
            foreach (TestGroup group in groups)
            {
                Log.AppendLine("GROUP: " + group.Key + "  " + group.Label +
                    (group.NeedsDatabase ? "  (需要游戏数据)" : "  (无需游戏数据)"));
            }
            return Finish(logPath,
                "共 " + groups.Count + " 组；用 -Group <key[,key]> 只跑其中几组。");
        }

        List<TestGroup> selected = SelectGroups(groups, ReadGroupFilter(args));
        if (selected.Count == 0)
            return Finish(logPath, "没有选中任何分组，未执行测试。");

        Lazy<CardDatabase> database = null;
        if (selected.Any(g => g.NeedsDatabase))
        {
            string dataRoot = DataPathResolver.Resolve(args);
            Check("data root found", !string.IsNullOrEmpty(dataRoot),
                dataRoot ?? string.Empty);
            if (string.IsNullOrEmpty(dataRoot))
                return Finish(logPath, "找不到游戏数据，依赖卡表的分组已跳过。");

            database = new Lazy<CardDatabase>(delegate
            {
                return CardDatabase.Load(dataRoot);
            });
        }

        foreach (TestGroup group in selected)
        {
            Log.AppendLine("--- " + group.Key + ": " + group.Label + " ---");
            try
            {
                group.Run(delegate { return database.Value; });
            }
            catch (Exception ex)
            {
                Fail("group " + group.Key + " crashed", ex.ToString());
            }
        }

        string[] skipped = groups
            .Where(g => !selected.Contains(g))
            .Select(g => g.Key)
            .ToArray();
        string summary = "GROUPS: " +
            string.Join(", ", selected.Select(g => g.Key).ToArray());
        if (skipped.Length > 0)
            summary += "；未运行: " + string.Join(", ", skipped);
        return Finish(logPath, summary);
    }

    private static void TestMetadataAndParser(CardDatabase database)
    {
        Check("series count >= 190", database.SeriesNames.Count >= 190,
            database.SeriesNames.Count.ToString());
        Check("series list is GBK sorted", IsSorted(database.SeriesNames),
            "first=" + (database.SeriesNames.Count > 0 ? database.SeriesNames[0] : ""));

        SeriesDefinition series = database.GetSeries("加速世界");
        Check("series 加速世界 loaded", series != null);
        if (series == null)
            return;

        Check("series folder name Accel World",
            series.FolderName == "Accel World", series.FolderName ?? string.Empty);
        Check("series abbreviation uses set prefix",
            series.Abbreviation == "AW", series.Abbreviation ?? string.Empty);
        Check("series lists every distinct abbreviation",
            series.AbbreviationText == "AW/GAW",
            series.AbbreviationText ?? string.Empty);

        SeriesDefinition keySeries = database.GetSeriesMetadata("Key");
        Check("series metadata Key available without loading cards",
            keySeries != null && !keySeries.CardsLoaded);
        Check("series folder name Key",
            keySeries != null && keySeries.FolderName == "Key",
            keySeries == null ? "missing" : keySeries.FolderName);
        Check("series keeps all Prefix lines in one folder",
            keySeries != null && keySeries.AbbreviationText.Contains("KTA"),
            keySeries == null ? "missing" : keySeries.AbbreviationText);

        SeriesDefinition blueArchive =
            database.GetSeriesMetadata("碧蓝档案");
        Check("series search matches name",
            SeriesSearchService.Matches(blueArchive, "碧蓝档案"));
        Check("series search matches abbreviation",
            SeriesSearchService.Matches(blueArchive, "BAV"));
        Check("series search matches card number head",
            SeriesSearchService.Matches(blueArchive, "w129"));
        SeriesDefinition touhou = database.GetSeriesMetadata("東方Project");
        Check("series search matches another card number head",
            SeriesSearchService.Matches(touhou, "s130"));
        Check("series search ignores whitespace",
            SeriesSearchService.Matches(blueArchive, "blue archive"));

        var coverLocator = new SeriesCoverLocator(database.CardsRoot);
        string coverPath = coverLocator.GetCoverPath(series);
        Check("series cover resolved to first card image",
            !string.IsNullOrEmpty(coverPath) && File.Exists(coverPath),
            coverPath ?? string.Empty);
        string missingCover = coverLocator.GetCoverPath(null);
        Check("series cover handles missing series",
            string.IsNullOrEmpty(missingCover), missingCover ?? string.Empty);

        SeriesFolder s18 = series.Folders.FirstOrDefault(
            f => (f.SetName ?? string.Empty).Contains("S18"));
        Check("S18 folder found", s18 != null);
        if (s18 == null)
            return;

        Check("S18 card count > 100", s18.Cards.Count > 100,
            s18.Cards.Count.ToString());

        WeissCard e101 = Find(s18, "AW/S18-E101");
        Check("E101 parsed", e101 != null);
        if (e101 != null)
        {
            Check("E101 color Green", e101.Color == CardColor.Green, e101.Color.ToString());
            Check("E101 level 0", e101.Level == 0, e101.Level.ToString());
            Check("E101 cost 0", e101.Cost == 0, e101.Cost.ToString());
            Check("E101 power 2500", e101.Power == 2500, e101.Power.ToString());
            Check("E101 trait スポーツ", e101.Traits[0] == "スポーツ", e101.Traits[0]);
            Check("E101 trait ネット", e101.Traits[1] == "ネット", e101.Traits[1]);
            Check("E101 text contains 战斗力+1500", e101.Text.Contains("战斗力+1500"));
            Check("E101 japanese name applied",
                e101.JapaneseName == "世話焼きな幼馴染 チユリ", e101.JapaneseName);
            Check("E101 image resolved",
                !string.IsNullOrEmpty(e101.ImagePath) && File.Exists(e101.ImagePath),
                e101.ImagePath ?? string.Empty);
        }

        WeissCard e102 = Find(s18, "AW/S18-E102");
        Check("E102 parsed", e102 != null);
        if (e102 != null)
        {
            Check("E102 level 2", e102.Level == 2, e102.Level.ToString());
            Check("E102 cost 1", e102.Cost == 1, e102.Cost.ToString());
            Check("E102 trigger Soul", e102.Trigger == TriggerIcon.Soul, e102.Trigger.ToString());
            Check("E102 trait アバター", e102.Traits[1] == "アバター", e102.Traits[1]);
        }

        WeissCard e104 = Find(s18, "AW/S18-E104");
        Check("E104 parsed", e104 != null);
        if (e104 != null)
        {
            Check("E104 level 3", e104.Level == 3, e104.Level.ToString());
            Check("E104 cost 2", e104.Cost == 2, e104.Cost.ToString());
            Check("E104 power 10000", e104.Power == 10000, e104.Power.ToString());
            Check("E104 soul 2", e104.Soul == 2, e104.Soul.ToString());
            Check("E104 trigger Soul", e104.Trigger == TriggerIcon.Soul, e104.Trigger.ToString());
        }

        WeissCard e027 = Find(s18, "AW/S18-E027");
        Check("climax shorthand parsed", e027 != null &&
            e027.Trigger == TriggerIcon.DoubleSoul,
            e027 == null ? "missing" : e027.Trigger.ToString());

        WeissCard e017 = Find(s18, "AW/S18-E017");
        WeissCard te01 = Find(s18, "AW/S18-TE01");
        Check("clone base TE01 parsed", te01 != null);
        Check("clone E017 parsed", e017 != null);
        if (e017 != null && te01 != null)
        {
            Check("E017 cloneOf TE01", e017.CloneOf == te01,
                e017.CloneOf == null ? "null" : e017.CloneOf.Id);
            Check("E017 copied name", e017.Name == te01.Name, e017.Name);
            Check("E017 copied traits", e017.Traits[0] == te01.Traits[0],
                e017.Traits[0]);
        }

        WeissCard e002 = Find(s18, "AW/S18-E002");
        WeissCard e002x = Find(s18, "AW/S18-E002X");
        Check("extra artwork E002X loaded", e002x != null);
        if (e002 != null && e002x != null)
        {
            Check("E002X cloneOf E002", e002x.CloneOf == e002,
                e002x.CloneOf == null ? "null" : e002x.CloneOf.Id);
            Check("E002X same name clone", e002x.IsSameNameClone, e002x.Name);
            Check("E002X uses alternate image",
                !string.IsNullOrEmpty(e002x.ImagePath) &&
                e002x.ImagePath.IndexOf("AlternateArtwork",
                    StringComparison.OrdinalIgnoreCase) >= 0,
                e002x.ImagePath ?? string.Empty);
        }

        Check("no empty card names", s18.Cards.All(c => !string.IsNullOrEmpty(c.Name)));
        int nonAltWarnings = database.Warnings.Count(w =>
            !w.StartsWith("No card found for alt artwork", StringComparison.Ordinal));
        Check("database parsing produced no folder errors", nonAltWarnings == 0,
            nonAltWarnings == 0 ? string.Empty : database.Warnings[0]);
    }

    private static void TestRealSearch(SeriesDefinition series)
    {
        if (series == null)
            return;

        var service = new CardSearchService();

        List<WeissCard> yellow = service.Search(series, new SearchQuery { Color = "Yellow" });
        Check("Yellow filter has results", yellow.Count > 0, yellow.Count.ToString());
        Check("Yellow filter only Yellow", yellow.All(c => c.Color == CardColor.Yellow));

        List<WeissCard> level3 = service.Search(series, new SearchQuery { Level = "3" });
        Check("Level 3 filter has E104", level3.Any(c => c.Id == "AW/S18-E104"));
        Check("Level 3 filter only level 3", level3.All(c => c.Level == 3));

        List<WeissCard> cost3 = service.Search(series, new SearchQuery { Cost = "3+" });
        Check("Cost 3+ filter only >= 3", cost3.All(c => c.Cost >= 3),
            cost3.Count.ToString());

        List<WeissCard> avatar = service.Search(series, new SearchQuery { Trait = "アバター" });
        Check("Trait アバター has results", avatar.Count > 0, avatar.Count.ToString());
        Check("Trait アバター exact", avatar.All(c =>
            c.Traits.Any(t => string.Equals(t, "アバター", StringComparison.Ordinal))));

        List<WeissCard> nameSearch = service.Search(series, new SearchQuery { CardName = "黒雪姫" });
        Check("Name search has results", nameSearch.Count > 0, nameSearch.Count.ToString());
        Check("Name search is substring", nameSearch.All(c => c.Name.Contains("黒雪姫")));

        List<WeissCard> textSearch = service.Search(series, new SearchQuery { Text = "战斗力" });
        Check("Text search has results", textSearch.Count > 0, textSearch.Count.ToString());
        Check("Text search is substring", textSearch.All(c => c.Text.Contains("战斗力")));

        List<WeissCard> climax = service.Search(series, new SearchQuery { Type = "Climax" });
        Check("Type Climax has results", climax.Count > 0, climax.Count.ToString());
        Check("Type Climax exact", climax.All(c => c.Category == CardCategory.Climax));

        List<WeissCard> idSearch = service.Search(series, new SearchQuery { CardName = "AW/S18-E101" });
        Check("Name search does not search id", idSearch.Count == 0, idSearch.Count.ToString());

        List<WeissCard> defaultAlt = service.Search(series, new SearchQuery());
        Check("default hides same-name clones", defaultAlt.All(c => !c.IsSameNameClone));
        Check("E017 hidden by default", !defaultAlt.Any(c => c.Id == "AW/S18-E017"));

        List<WeissCard> showAlt = service.Search(series, new SearchQuery { ShowAltArt = true });
        Check("show alt includes E017", showAlt.Any(c => c.Id == "AW/S18-E017"),
            showAlt.Count.ToString());
        Check("show alt includes E002X", showAlt.Any(c => c.Id == "AW/S18-E002X"),
            showAlt.Count.ToString());

        List<WeissCard> onlyAlt = service.Search(series, new SearchQuery
        {
            ShowAltArt = true,
            OnlyAltArt = true
        });
        Check("only alt has results", onlyAlt.Count > 0, onlyAlt.Count.ToString());
        Check("only alt only same-name clones", onlyAlt.All(c => c.IsSameNameClone));
        Check("only alt includes E017", onlyAlt.Any(c => c.Id == "AW/S18-E017"));
        Check("only alt includes E002X", onlyAlt.Any(c => c.Id == "AW/S18-E002X"));

        List<WeissCard> invalidAlt = service.Search(series, new SearchQuery
        {
            ShowAltArt = false,
            OnlyAltArt = true
        });
        Check("only alt with show off returns none", invalidAlt.Count == 0,
            invalidAlt.Count.ToString());

        SeriesFolder s18 = series.Folders.FirstOrDefault(
            f => (f.SetName ?? string.Empty).Contains("S18"));
        if (s18 != null)
        {
            List<WeissCard> setFiltered = service.Search(series, new SearchQuery
            {
                SetName = "加速世界 (S18)",
                ShowAltArt = true
            });
            Check("set filter returns exactly S18 folder",
                setFiltered.Count == s18.Cards.Count,
                setFiltered.Count + "/" + s18.Cards.Count);
        }

        Check("search all count matches card count", defaultAlt.Count <= series.CardCount,
            defaultAlt.Count + "/" + series.CardCount);
    }

    private static void TestRealSorts(SeriesDefinition series)
    {
        if (series == null)
            return;

        var service = new CardSearchService();
        var allQuery = new SearchQuery { ShowAltArt = true };

        List<WeissCard> sort0 = service.Search(series, new SearchQuery
        {
            ShowAltArt = true,
            SortMode = CardSortMode.ColorCategoryLevelCostPowerName
        });
        Check("sort 0 returns cards", sort0.Count > 0, sort0.Count.ToString());
        Check("sort 0 ordered by color/category/level/cost/power/name",
            IsSortedBy(sort0, CompareSort0));

        List<WeissCard> sort1 = service.Search(series, new SearchQuery
        {
            ShowAltArt = true,
            SortMode = CardSortMode.CardNumber
        });
        Check("sort 1 ordered by card number", IsSortedBy(sort1, CompareSort1));

        List<WeissCard> sort2 = service.Search(series, new SearchQuery
        {
            ShowAltArt = true,
            SortMode = CardSortMode.Name
        });
        Check("sort 2 ordered by normalized name", IsSortedBy(sort2, CompareSort2));

        List<WeissCard> sort3 = service.Search(series, new SearchQuery
        {
            ShowAltArt = true,
            SortMode = CardSortMode.Trait
        });
        List<WeissCard> all = service.Search(series, allQuery);
        var expectedIds = new HashSet<string>(all.Select(c => c.Id));
        var actualIds = new HashSet<string>(sort3.Select(c => c.Id));
        Check("sort 3 keeps every card", expectedIds.SetEquals(actualIds),
            "expected " + expectedIds.Count + " actual " + actualIds.Count);
        Check("sort 3 may duplicate cards",
            sort3.Count >= actualIds.Count,
            "rows=" + sort3.Count + " distinct=" + actualIds.Count);
    }

    private static void TestSyntheticSearches()
    {
        var series = new SeriesDefinition("Synthetic");
        var main = new SeriesFolder
        {
            SeriesName = "Synthetic",
            SetName = "Only",
            FolderPath = "Synthetic"
        };
        var emptySet = new SeriesFolder
        {
            SeriesName = "Synthetic",
            SetName = string.Empty,
            FolderPath = "Synthetic/Empty"
        };
        series.Folders.Add(main);
        series.Folders.Add(emptySet);

        WeissCard purple = NewCard("SYN/001", "Purple Level Four",
            CardColor.Purple, 4, 3, 9000, "TraitA", string.Empty);
        WeissCard costTwo = NewCard("SYN/002", "Cost Two",
            CardColor.Red, 1, 2, 3000, "TraitA", string.Empty);
        WeissCard accent = NewCard("SYN/003", "ångström",
            CardColor.Blue, 0, 0, 1000, string.Empty, string.Empty);
        WeissCard textCard = NewCard("SYN/004", "Text Card",
            CardColor.Green, 0, 0, 1000, string.Empty, string.Empty);
        textCard.Text = "Hello World";
        WeissCard partialTrait = NewCard("SYN/005", "Partial Trait",
            CardColor.Yellow, 0, 0, 1000, "TraitAB", string.Empty);
        WeissCard undefined = NewCard("SYN/006", "Undefined Color",
            CardColor.Undefined, 0, 0, 0, string.Empty, string.Empty);
        WeissCard emptySetCard = NewCard("SYN/007", "Empty Set Name",
            CardColor.Yellow, 0, 0, 1000, string.Empty, string.Empty);

        main.Cards.Add(purple);
        main.Cards.Add(costTwo);
        main.Cards.Add(accent);
        main.Cards.Add(textCard);
        main.Cards.Add(partialTrait);
        main.Cards.Add(undefined);
        emptySet.Cards.Add(emptySetCard);

        WeissCard altBase = NewCard("SYN/100", "Alt Base",
            CardColor.Red, 1, 1, 5000, "AltTrait", string.Empty);
        var altClone = new WeissCard();
        altClone.Id = "SYN/100X";
        altClone.SeriesName = "Synthetic";
        altClone.SetName = "Only";
        altClone.CopyFrom(altBase);
        main.Cards.Add(altBase);
        main.Cards.Add(altClone);

        var service = new CardSearchService();

        List<WeissCard> purpleResult = service.Search(series, new SearchQuery
        {
            Color = "Purple",
            Level = "4",
            Cost = "3+"
        });
        Check("synthetic purple/level4/cost3+", purpleResult.Contains(purple),
            purpleResult.Count.ToString());

        List<WeissCard> costResult = service.Search(series, new SearchQuery { Cost = "3+" });
        Check("synthetic 3+ excludes cost 2", !costResult.Contains(costTwo));

        List<WeissCard> accentResult = service.Search(series, new SearchQuery
        {
            CardName = "engström"
        });
        Check("synthetic å normalized in name", accentResult.Contains(accent),
            accentResult.Count.ToString());

        List<WeissCard> textResult = service.Search(series, new SearchQuery
        {
            Text = " world "
        });
        Check("synthetic text search trims", textResult.Contains(textCard),
            textResult.Count.ToString());

        List<WeissCard> traitResult = service.Search(series, new SearchQuery
        {
            Trait = "TraitA"
        });
        Check("synthetic trait exact", traitResult.Contains(purple) &&
            !traitResult.Contains(partialTrait));

        List<WeissCard> setResult = service.Search(series, new SearchQuery
        {
            SetName = "Only"
        });
        Check("synthetic empty setName folder always included",
            setResult.Contains(emptySetCard), setResult.Count.ToString());

        List<WeissCard> allResult = service.Search(series, new SearchQuery());
        Check("synthetic Undefined included under 所有", allResult.Contains(undefined));

        List<WeissCard> idSearch = service.Search(series, new SearchQuery
        {
            CardName = "SYN/001"
        });
        Check("synthetic name search ignores id", idSearch.Count == 0,
            idSearch.Count.ToString());

        List<WeissCard> defaultAlt = service.Search(series, new SearchQuery
        {
            CardName = "Alt Base"
        });
        Check("synthetic default alt hidden", defaultAlt.Count == 1 &&
            defaultAlt[0] == altBase, defaultAlt.Count.ToString());

        List<WeissCard> showAlt = service.Search(series, new SearchQuery
        {
            CardName = "Alt Base",
            ShowAltArt = true
        });
        Check("synthetic show alt includes clone", showAlt.Count == 2,
            showAlt.Count.ToString());

        List<WeissCard> onlyAlt = service.Search(series, new SearchQuery
        {
            ShowAltArt = true,
            OnlyAltArt = true
        });
        Check("synthetic only alt", onlyAlt.Count == 1 && onlyAlt[0] == altClone,
            onlyAlt.Count.ToString());

        List<WeissCard> multiColor = service.Search(series, new SearchQuery
        {
            Colors = { "Yellow", "Purple" }
        });
        Check("synthetic multi color OR", multiColor.Contains(purple) &&
            multiColor.Any(c => c.Color == CardColor.Yellow) &&
            multiColor.All(c => c.Color == CardColor.Yellow ||
                c.Color == CardColor.Purple),
            multiColor.Count.ToString());

        List<WeissCard> multiGroup = service.Search(series, new SearchQuery
        {
            Colors = { "Purple", "Red" },
            Levels = { "4" }
        });
        Check("synthetic multi filters AND", multiGroup.Count == 1 &&
            multiGroup[0] == purple, multiGroup.Count.ToString());

        List<WeissCard> multiCost = service.Search(series, new SearchQuery
        {
            Costs = { "2", "3+" }
        });
        Check("synthetic multi cost OR", multiCost.Contains(costTwo) &&
            multiCost.Contains(purple) && multiCost.All(c => c.Cost >= 2),
            multiCost.Count.ToString());
    }

    private static void TestSyntheticTraitSort()
    {
        var series = new SeriesDefinition("TraitSort");
        var folder = new SeriesFolder
        {
            SeriesName = "TraitSort",
            SetName = "TraitSort",
            FolderPath = "TraitSort"
        };
        series.Folders.Add(folder);

        WeissCard c1 = NewCard("TR/001", "C1", CardColor.Yellow, 0, 0, 1000, "A", string.Empty);
        WeissCard c2 = NewCard("TR/002", "C2", CardColor.Yellow, 0, 0, 1000, "A", string.Empty);
        WeissCard c3 = NewCard("TR/003", "C3", CardColor.Yellow, 0, 0, 1000, "A", string.Empty);
        WeissCard c4 = NewCard("TR/004", "C4", CardColor.Yellow, 0, 0, 1000, "A", "B");
        WeissCard c5 = NewCard("TR/005", "C5", CardColor.Yellow, 0, 0, 1000, "B", string.Empty);
        folder.Cards.Add(c1);
        folder.Cards.Add(c2);
        folder.Cards.Add(c3);
        folder.Cards.Add(c4);
        folder.Cards.Add(c5);

        var service = new CardSearchService();
        List<WeissCard> duplicated = service.Search(series, new SearchQuery
        {
            SortMode = CardSortMode.Trait
        });
        Check("trait sort produces six rows", duplicated.Count == 6,
            duplicated.Count.ToString());
        Check("trait sort duplicates multi-group card",
            duplicated.Count(c => c.Id == "TR/004") == 2,
            duplicated.Count(c => c.Id == "TR/004").ToString());
        Check("trait sort keeps every card",
            new HashSet<string>(duplicated.Select(c => c.Id)).Count == 5);

        var thresholdSeries = new SeriesDefinition("Threshold");
        var thresholdFolder = new SeriesFolder
        {
            SeriesName = "Threshold",
            SetName = "Threshold",
            FolderPath = "Threshold"
        };
        thresholdSeries.Folders.Add(thresholdFolder);
        for (int i = 1; i <= 5; i++)
        {
            thresholdFolder.Cards.Add(NewCard("TH/00" + i, "A" + i,
                CardColor.Yellow, 0, 0, 1000, "A", string.Empty));
        }
        WeissCard c6 = NewCard("TH/006", "C6",
            CardColor.Yellow, 0, 0, 1000, "C", string.Empty);
        thresholdFolder.Cards.Add(c6);

        List<WeissCard> threshold = service.Search(thresholdSeries, new SearchQuery
        {
            SortMode = CardSortMode.Trait
        });
        Check("trait group above 0.8 threshold is not split",
            threshold.Count == 6, threshold.Count.ToString());
        Check("trait group above threshold sorted with remainder",
            threshold[0] == c6, threshold[0].Id);
    }

    private static WeissCard NewCard(
        string id,
        string name,
        CardColor color,
        int level,
        int cost,
        int power,
        string trait1,
        string trait2)
    {
        var card = new WeissCard();
        card.Id = id;
        card.Name = name;
        card.Color = color;
        card.Level = level;
        card.Cost = cost;
        card.Power = power;
        card.Category = CardCategory.Character;
        card.Traits[0] = trait1;
        card.Traits[1] = trait2;
        return card;
    }

    private static WeissCard Find(SeriesFolder folder, string id)
    {
        return folder.Cards.FirstOrDefault(c =>
            string.Equals(c.Id, id, StringComparison.Ordinal));
    }

    private static bool IsSorted(IList<string> values)
    {
        for (int i = 1; i < values.Count; i++)
        {
            if (GbkStringComparer.Instance.Compare(values[i - 1], values[i]) > 0)
                return false;
        }
        return true;
    }

    private static bool IsSortedBy<T>(List<T> values, Func<T, T, int> compare)
    {
        for (int i = 1; i < values.Count; i++)
        {
            if (compare(values[i - 1], values[i]) > 0)
                return false;
        }
        return true;
    }

    private static int CompareSort0(WeissCard left, WeissCard right)
    {
        int result = ((int)left.Color).CompareTo((int)right.Color);
        if (result != 0) return result;
        result = ((int)left.Category).CompareTo((int)right.Category);
        if (result != 0) return result;
        result = left.Level.CompareTo(right.Level);
        if (result != 0) return result;
        result = left.Cost.CompareTo(right.Cost);
        if (result != 0) return result;
        result = left.Power.CompareTo(right.Power);
        if (result != 0) return result;
        return string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
    }

    private static int CompareSort1(WeissCard left, WeissCard right)
    {
        return string.Compare(
            NormalizeCardNumber(left.Id), NormalizeCardNumber(right.Id),
            StringComparison.CurrentCulture);
    }

    private static int CompareSort2(WeissCard left, WeissCard right)
    {
        return string.Compare(
            NormalizeName(left.Name), NormalizeName(right.Name),
            StringComparison.CurrentCulture);
    }

    private static string NormalizeCardNumber(string id)
    {
        return (id ?? string.Empty).Replace("-E", "-").Replace("-PE", "-P");
    }

    private static string NormalizeName(string name)
    {
        return (name ?? string.Empty).ToLower().Replace("\"", string.Empty);
    }

    /// <summary>
    /// The Sanrio set ships English effect texts with ASCII double quotes
    /// around Japanese card names. Before the matcher handled that quoting
    /// style the detail page showed no related cards at all.
    /// </summary>
    private static void TestRelatedCardResolution(CardDatabase database)
    {
        SeriesDefinition sanrio = null;
        for (int i = 0; i < database.SeriesNames.Count; i++)
        {
            SeriesDefinition metadata =
                database.GetSeriesMetadata(database.SeriesNames[i]);
            if (metadata == null ||
                !string.Equals(metadata.FolderName, "Sanrio",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sanrio = database.GetSeries(database.SeriesNames[i]);
            break;
        }

        Check("sanrio series loaded for name resolution",
            sanrio != null && sanrio.CardsLoaded);
        if (sanrio == null)
            return;

        const string text =
            "Text Cont: All of your other \"Blau Party ポムポムプリン\" " +
            "gets +1000 power.";
        List<string> names = CardNameMatcher.ExtractNames(text);
        Check("english text yields quoted japanese name",
            names.Count == 1 && names[0] == "Blau Party ポムポムプリン",
            string.Join("|", names.ToArray()));
        if (names.Count == 0)
            return;

        List<WeissCard> resolved = CardNameMatcher.Resolve(
            sanrio.EnumerateCards(), names[0], 4);
        Check("quoted japanese name resolves inside sanrio",
            resolved.Count > 0 && resolved[0].Name == "Blau Party ポムポムプリン",
            resolved.Count == 0 ? "no match" : resolved[0].Name);
    }

    private static void TestCardNameMatcher()
    {
        Check("name normalize removes ascii quotes and spaces",
            CardNameMatcher.Normalize("\"Pajama Party\" Kanaho") ==
                "pajamapartykanaho",
            CardNameMatcher.Normalize("\"Pajama Party\" Kanaho"));
        Check("name normalize strips chinese quotes",
            CardNameMatcher.Normalize("“あたたかな日々”") == "あたたかな日々",
            CardNameMatcher.Normalize("“あたたかな日々”"));
        Check("name normalize folds full width ampersand",
            CardNameMatcher.Normalize("フィン＆ジェイク") ==
                CardNameMatcher.Normalize("フィン&ジェイク"),
            CardNameMatcher.Normalize("フィン＆ジェイク"));

        Check("quote style difference still matches exactly",
            CardNameMatcher.Score(
                "\"To Protect That Smile\" Kanaho",
                "'To Protect That Smile' Kanaho") == 0);
        Check("ascii quote reaches name carrying chinese quotes",
            CardNameMatcher.Score("“あたたかな日々”", "\"あたたかな日々\"") == 0);
        Check("quoted short title matches longer name",
            CardNameMatcher.Score("\"Pajama Party\" Kanaho", "Pajama Party") == 1);
        Check("unrelated name is rejected",
            CardNameMatcher.Score("チアリーチャム", "Blau Party") < 0);

        List<WeissCard> ordered = CardNameMatcher.Resolve(new[]
        {
            MakeCard("AT/WX02-001", "Finn the Human & Jake the Dog"),
            MakeCard("AT/WX02-002", "Finn the Human")
        }, "Finn the Human", 4);
        Check("exact match ranked before prefix match",
            ordered.Count == 2 && ordered[0].Id == "AT/WX02-002",
            ordered.Count == 0 ? "no match" : ordered[0].Id);

        var sanrioCards = new[]
        {
            MakeCard("SN/01-004", "Blau Party ポムポムプリン"),
            MakeCard("SN/01-005", "チアリーチャム")
        };
        List<WeissCard> sanrio = CardNameMatcher.Resolve(
            sanrioCards, "Blau Party ポムポムプリン", 4);
        Check("english effect name resolves japanese card",
            sanrio.Count == 1 && sanrio[0].Id == "SN/01-004",
            sanrio.Count == 0 ? "no match" : sanrio[0].Id);

        List<WeissCard> asciiQuoted = CardNameMatcher.Resolve(
            new[] { MakeCard("SN/01-006", "“あたたかな日々”") },
            "\"あたたかな日々\"", 4);
        Check("ascii quoted climax resolves card with chinese quotes",
            asciiQuoted.Count == 1 && asciiQuoted[0].Id == "SN/01-006");

        WeissCard localCard = MakeCard("ALT/01-002", "Pom Pom Purin");
        localCard.FolderPath = "Cards/Sanrio/01";
        WeissCard otherCard = MakeCard("ALT/09-002", "Pom Pom Purin");
        otherCard.FolderPath = "Cards/Sanrio/09";
        List<WeissCard> preferred = CardNameMatcher.Resolve(
            new[] { otherCard, localCard }, "Pom Pom Purin", 4,
            "Cards/Sanrio/01");
        Check("same folder wins among equal scores",
            preferred.Count == 2 && preferred[0].Id == "ALT/01-002",
            preferred.Count == 0 ? "no match" : preferred[0].Id);

        List<string> names = CardNameMatcher.ExtractNames(
            "When \"The Call of Adventure\" is placed, choose 「午休」 " +
            "or 『“作为恋人…”漆原琉华』.");
        Check("extract ascii quoted name",
            names.Contains("The Call of Adventure"),
            string.Join("|", names.ToArray()));
        Check("extract japanese quoted name", names.Contains("午休"),
            string.Join("|", names.ToArray()));
        Check("extract name that contains inner quotes",
            names.Contains("“作为恋人…”漆原琉华"),
            string.Join("|", names.ToArray()));

        List<string> recovered = CardNameMatcher.ExtractNames(
            "broken 「name “Frieren”");
        Check("unmatched opener does not hide later names",
            recovered.Contains("Frieren"),
            string.Join("|", recovered.ToArray()));
    }

    /// <summary>
    /// The deck pages read StreamingAssets\Decks and DefaultDecks. These checks
    /// cover the folder scan, the line parsing and the cover lookup the grid
    /// uses, so a broken deck file format shows up before the UI does.
    /// </summary>
    private static void TestDeckLibrary(CardDatabase database)
    {
        IList<string> parsed = DeckLibrary.ParseCardCodes(new[]
        {
            "MTI/S83-E101",
            "  MTI/S83-E025  ",
            string.Empty,
            "# comment",
            "// comment",
            "garbage line"
        });
        Check("deck parser keeps card codes only", parsed.Count == 2,
            string.Join("|", parsed.ToArray()));
        Check("deck parser trims surrounding spaces",
            parsed.Count == 2 && parsed[1] == "MTI/S83-E025",
            parsed.Count < 2 ? "missing" : parsed[1]);

        DeckLibrary library = DeckLibrary.Load(database.DataRoot);
        Check("deck library finds default decks", library.DefaultCount >= 15,
            library.DefaultCount.ToString());
        Check("deck library finds the local deck", library.LocalCount >= 1,
            library.LocalCount.ToString());
        Check("deck library reports no unreadable decks",
            library.Warnings.Count == 0,
            library.Warnings.Count == 0 ? string.Empty : library.Warnings[0]);

        DeckDefinition mushoku = null;
        for (int i = 0; i < library.Decks.Count; i++)
        {
            if (string.Equals(library.Decks[i].Name, "AI_Mushoku",
                StringComparison.Ordinal))
            {
                mushoku = library.Decks[i];
                break;
            }
        }

        Check("default deck AI_Mushoku parsed", mushoku != null);
        if (mushoku != null)
        {
            Check("AI_Mushoku has 50 cards", mushoku.CardCount == 50,
                mushoku.CardCount.ToString());
            Check("AI_Mushoku keeps the file order",
                mushoku.CardCodes[0] == "MTI/S83-E101",
                mushoku.CardCount == 0 ? "empty" : mushoku.CardCodes[0]);
            Check("AI_Mushoku abbreviation is MTI",
                mushoku.PrimaryAbbreviation == "MTI",
                mushoku.PrimaryAbbreviation);
            Check("AI_Mushoku is a default deck",
                mushoku.Source == DeckSource.Default &&
                string.Equals(Path.GetFileName(Path.GetDirectoryName(mushoku.FilePath)),
                    DeckLibrary.DefaultFolderName, StringComparison.OrdinalIgnoreCase),
                mushoku.FilePath);
        }

        Check("deck names drop the file extension",
            library.Decks.All(d => !d.Name.EndsWith(".txt",
                StringComparison.OrdinalIgnoreCase)));
        Check("every parsed deck has card codes",
            library.Decks.All(d => d.CardCount > 0),
            library.Decks.Count(d => d.CardCount == 0).ToString());

        var covers = new DeckCoverLocator(database);
        string mushokuCover = mushoku == null ? null : covers.GetCoverPath(mushoku);
        Check("deck cover uses the first card image",
            !string.IsNullOrEmpty(mushokuCover) && File.Exists(mushokuCover) &&
            Path.GetFileName(mushokuCover).StartsWith("MTI_S83_E101",
                StringComparison.OrdinalIgnoreCase),
            mushokuCover ?? "missing");

        int covered = library.Decks.Count(d =>
            !string.IsNullOrEmpty(covers.GetCoverPath(d)));
        Check("almost every deck gets a cover image",
            covered >= library.Decks.Count - 2,
            covered + "/" + library.Decks.Count);
    }

    /// <summary>
    /// Backs the 展开 / 缩略 views of the deck detail page: cards are ordered
    /// by code, duplicates collapse into a count, and every code resolves to
    /// an image even when only the base art file exists.
    /// </summary>
    private static void TestDeckComposition(CardDatabase database)
    {
        var codes = new List<string>
        {
            "MTI/S83-E101",
            "DAL/W79-E001",
            "MTI/S83-E101",
            "DAL/W79-E001",
            "MTI/S83-E101"
        };

        IList<string> ordered = DeckComposition.OrderByCode(codes);
        Check("deck view sorts cards by code",
            ordered.Count == 5 &&
            ordered[0] == "DAL/W79-E001" &&
            ordered[1] == "DAL/W79-E001" &&
            ordered[2] == "MTI/S83-E101",
            string.Join("|", ordered.ToArray()));

        IList<DeckCardCount> groups = DeckComposition.GroupByCode(codes);
        Check("compact deck view collapses duplicate codes",
            groups.Count == 2 &&
            groups[0].Code == "DAL/W79-E001" && groups[0].Count == 2 &&
            groups[1].Code == "MTI/S83-E101" && groups[1].Count == 3,
            string.Join("|", groups.Select(g => g.Code + " x" + g.Count).ToArray()));

        IList<DeckCardCount> caseGroups = DeckComposition.GroupByCode(
            new[] { "MTI/S83-E101", "mti/s83-e101" });
        Check("compact deck view ignores code case",
            caseGroups.Count == 1 && caseGroups[0].Count == 2,
            caseGroups.Count.ToString());

        Check("base code strips the parallel art suffix",
            DeckComposition.GetBaseCode("MTI/S83-E055S") == "MTI/S83-E055" &&
            DeckComposition.GetBaseCode("MTI/S83-E055") == "MTI/S83-E055" &&
            DeckComposition.GetBaseCode(" MTI/S83-E055S ") == "MTI/S83-E055" &&
            DeckComposition.GetBaseCode(null) == string.Empty,
            DeckComposition.GetBaseCode("MTI/S83-E055S"));

        IList<DeckCardCount> unified = DeckComposition.GroupByBaseCode(new[]
        {
            "MTI/S83-E055",
            "MTI/S83-E055S",
            "MTI/S83-E055S",
            "DAL/W79-E001"
        });
        Check("unified art merges parallel prints into the normal code",
            unified.Count == 2 &&
            unified[0].Code == "DAL/W79-E001" && unified[0].Count == 1 &&
            unified[1].Code == "MTI/S83-E055" && unified[1].Count == 3,
            string.Join("|", unified.Select(
                g => g.Code + " x" + g.Count).ToArray()));
        Check("unified art treats an empty deck like the plain grouping",
            DeckComposition.GroupByBaseCode(null).Count == 0);

        Check("ordering an empty deck stays empty",
            DeckComposition.OrderByCode(null).Count == 0 &&
            DeckComposition.GroupByCode(null).Count == 0);

        IList<string> distinct = DeckComposition.DistinctCards(codes);
        Check("detail arrows walk every card once",
            distinct.Count == 2 &&
            distinct[0] == "DAL/W79-E001" &&
            distinct[1] == "MTI/S83-E101",
            string.Join("|", distinct.ToArray()));

        IList<string> distinctArt = DeckComposition.DistinctCards(new[]
        {
            "MTI/S83-E055S",
            "MTI/S83-E055",
            "mti/s83-055"
        });
        Check("detail arrows fold art variants and code case",
            distinctArt.Count == 1 &&
            string.Equals(CardCodeMatcher.Canonical(distinctArt[0]),
                "MTI/S83-055", StringComparison.OrdinalIgnoreCase),
            string.Join("|", distinctArt.ToArray()));

        IList<string> distinctMixed = DeckComposition.DistinctCards(new[]
        {
            "DAL/W79-E001",
            "DAL/W79-E001",
            "MTI/S83-E055S",
            "MTI/S83-E055",
            "MTI/S83-E101",
            "DAL/W79-E001"
        });
        bool movesOn = distinctMixed.Count == 3;
        for (int i = 1; movesOn && i < distinctMixed.Count; i++)
        {
            movesOn = !string.Equals(
                CardCodeMatcher.Canonical(distinctMixed[i - 1]),
                CardCodeMatcher.Canonical(distinctMixed[i]),
                StringComparison.OrdinalIgnoreCase);
        }

        Check("detail arrows never stop on the same card twice", movesOn,
            string.Join("|", distinctMixed.ToArray()));
        Check("detail arrows handle an empty deck",
            DeckComposition.DistinctCards(null).Count == 0);

        DeckLibrary library = DeckLibrary.Load(database.DataRoot);
        DeckDefinition deck = null;
        for (int i = 0; i < library.Decks.Count; i++)
        {
            if (string.Equals(library.Decks[i].Name, "AI_Mushoku",
                StringComparison.OrdinalIgnoreCase))
            {
                deck = library.Decks[i];
                break;
            }
        }

        Check("deck detail test deck found", deck != null,
            library.Decks.Count == 0
                ? "no decks"
                : string.Join("|", library.Decks.Select(d => d.Name).ToArray()));
        if (deck == null)
            return;

        IList<string> deckOrder = DeckComposition.OrderByCode(deck.CardCodes);
        Check("expanded deck view keeps every copy",
            deckOrder.Count == deck.CardCount && deck.CardCount == 50,
            deckOrder.Count + "/" + deck.CardCount);

        IList<DeckCardCount> deckGroups =
            DeckComposition.GroupByCode(deck.CardCodes);
        int total = 0;
        bool sorted = true;
        for (int i = 0; i < deckGroups.Count; i++)
        {
            total += deckGroups[i].Count;
            if (i > 0 && string.CompareOrdinal(
                deckGroups[i - 1].Code, deckGroups[i].Code) > 0)
            {
                sorted = false;
            }
        }

        Check("compact deck view keeps the card total",
            total == deck.CardCount, total + "/" + deck.CardCount);
        Check("compact deck view is sorted by code", sorted);
        Check("compact deck view is shorter than the deck",
            deckGroups.Count >= 1 && deckGroups.Count < deck.CardCount,
            deckGroups.Count + "/" + deck.CardCount);

        IList<DeckCardCount> unifiedGroups =
            DeckComposition.GroupByBaseCode(deck.CardCodes);
        int unifiedTotal = 0;
        bool unifiedSorted = true;
        for (int i = 0; i < unifiedGroups.Count; i++)
        {
            unifiedTotal += unifiedGroups[i].Count;
            if (i > 0 && string.CompareOrdinal(
                unifiedGroups[i - 1].Code, unifiedGroups[i].Code) > 0)
            {
                unifiedSorted = false;
            }
        }

        Check("unified art keeps the card total",
            unifiedTotal == deck.CardCount, unifiedTotal + "/" + deck.CardCount);
        Check("unified art is sorted by base code", unifiedSorted);
        Check("unified art never creates new groups",
            unifiedGroups.Count <= deckGroups.Count,
            unifiedGroups.Count + "/" + deckGroups.Count);

        var images = new DeckCoverLocator(database);
        string firstCard = images.GetCardImagePath("MTI/S83-E101");
        Check("deck card image matches the cover file",
            !string.IsNullOrEmpty(firstCard) && File.Exists(firstCard) &&
            Path.GetFileName(firstCard).StartsWith("MTI_S83_E101",
                StringComparison.OrdinalIgnoreCase),
            firstCard ?? "missing");

        string parallel = images.GetCardImagePath("MTI/S83-E055S");
        Check("parallel card falls back to the base art",
            !string.IsNullOrEmpty(parallel) && File.Exists(parallel) &&
            Path.GetFileName(parallel).StartsWith("MTI_S83_E055",
                StringComparison.OrdinalIgnoreCase),
            parallel ?? "missing");

        Check("unknown card has no image",
            string.IsNullOrEmpty(images.GetCardImagePath("ZZZ/00-000")));

        int resolved = 0;
        for (int i = 0; i < deckOrder.Count; i++)
        {
            if (!string.IsNullOrEmpty(images.GetCardImagePath(deckOrder[i])))
                resolved++;
        }

        Check("every card of the test deck has an image",
            resolved == deck.CardCount, resolved + "/" + deck.CardCount);

        IList<SeriesDefinition> series = images.ResolveSeries(deck);
        Check("deck detail resolves its series",
            series.Count > 0 && !string.IsNullOrEmpty(series[0].Name),
            series.Count == 0 ? "none" : series[0].Name);
        Check("deck detail lists the MTI prefix",
            series.Any(s => s.Abbreviations.Contains(
                "MTI", StringComparer.OrdinalIgnoreCase)),
            series.Count == 0 ? "none" : series[0].AbbreviationText);

        var resolver = new DeckCardResolver(database, images);
        int resolvedCards = 0;
        for (int i = 0; i < deckOrder.Count; i++)
        {
            if (resolver.Resolve(deck, deckOrder[i]) != null)
                resolvedCards++;
        }

        Check("deck detail resolves every card of the test deck",
            resolvedCards == deck.CardCount,
            resolvedCards + "/" + deck.CardCount);

        string resolvedImage = deckOrder.Count == 0
            ? null
            : resolver.GetImagePath(deck, deckOrder[0]);
        Check("deck detail image comes from the parsed card",
            !string.IsNullOrEmpty(resolvedImage) && File.Exists(resolvedImage),
            resolvedImage ?? "missing");

        Check("card code folds the parallel art E",
            CardCodeMatcher.Canonical("NGL/S58-E001") ==
            CardCodeMatcher.Canonical("NGL/S58-001"));
        Check("card code folds the trial E",
            CardCodeMatcher.Canonical("KS/W49-TE16") ==
            CardCodeMatcher.Canonical("KS/W49-T16"));
        Check("card code drops the rarity letters",
            CardCodeMatcher.Canonical("GBS/S63-E068S") ==
            CardCodeMatcher.Canonical("GBS/S63-068"));
        Check("card code keeps different numbers apart",
            CardCodeMatcher.Canonical("KS/W49-040") !=
            CardCodeMatcher.Canonical("KS/W49-041"));

        Check("image name adds the parallel E",
            CardCodeMatcher.EnsureParallelE("KS/W49-003") == "KS/W49-E003",
            CardCodeMatcher.EnsureParallelE("KS/W49-003"));
        Check("image name adds the trial E",
            CardCodeMatcher.EnsureParallelE("KS/W49-T16") == "KS/W49-TE16",
            CardCodeMatcher.EnsureParallelE("KS/W49-T16"));
        Check("image name adds the promo E",
            CardCodeMatcher.EnsureParallelE("KS/W49-P06") == "KS/W49-PE06",
            CardCodeMatcher.EnsureParallelE("KS/W49-P06"));
        Check("image name keeps a code that already has the E",
            CardCodeMatcher.EnsureParallelE("MTI/S83-E101") == "MTI/S83-E101",
            CardCodeMatcher.EnsureParallelE("MTI/S83-E101"));

        Check("text reader keeps UTF-8 files",
            TextFileReader.DetectEncoding(
                Encoding.UTF8.GetBytes("测试 UTF-8")).CodePage == 65001);
        Check("text reader falls back to GBK",
            TextFileReader.DetectEncoding(
                new byte[] { 0xB2, 0xE2, 0xCA, 0xD4 }).CodePage != 65001);
    }

    /// <summary>
    /// 卡组信息 numbers: the type / colour matrix, the level histogram that
    /// skips 高潮 cards and the 魂 / 2魂 trigger counts.
    /// </summary>
    private static void TestDeckStatistics()
    {
        var cards = new Dictionary<string, WeissCard>(
            StringComparer.OrdinalIgnoreCase);
        AddStatCard(cards, "T/A-01", CardCategory.Character, CardColor.Yellow, 0, TriggerIcon.Soul);
        AddStatCard(cards, "T/A-02", CardCategory.Event, CardColor.Yellow, 1, TriggerIcon.DoubleSoul);
        AddStatCard(cards, "T/A-03", CardCategory.Character, CardColor.Green, 2, TriggerIcon.None);
        AddStatCard(cards, "T/A-04", CardCategory.Character, CardColor.Red, 3, TriggerIcon.None);
        AddStatCard(cards, "T/A-05", CardCategory.Climax, CardColor.Red, 0, TriggerIcon.Soul);
        AddStatCard(cards, "T/A-06", CardCategory.Character, CardColor.Blue, 0, TriggerIcon.Soul);
        AddStatCard(cards, "T/A-07", CardCategory.Character, CardColor.Purple, 1, TriggerIcon.None);

        var codes = new List<string>
        {
            "T/A-01", "T/A-01", "T/A-02", "T/A-03", "T/A-04",
            "T/A-05", "T/A-06", "T/A-06", "T/A-07", "T/MISSING"
        };

        DeckStatistics stats = DeckStatistics.Compute(codes, delegate(string code)
        {
            WeissCard card;
            return cards.TryGetValue(code, out card) ? card : null;
        });

        Check("deck stats count every resolved card",
            stats.Total == 9 && stats.Unresolved == 1,
            stats.Total + "/" + stats.Unresolved);

        Check("deck stats split the categories",
            stats.GetCategoryTotal(CardCategory.Character) == 7 &&
            stats.GetCategoryTotal(CardCategory.Event) == 1 &&
            stats.GetCategoryTotal(CardCategory.Climax) == 1,
            stats.GetCategoryTotal(CardCategory.Character) + "/" +
            stats.GetCategoryTotal(CardCategory.Event) + "/" +
            stats.GetCategoryTotal(CardCategory.Climax));

        Check("deck stats keep the four matrix colours",
            stats.GetColorTotal(CardColor.Yellow) == 3 &&
            stats.GetColorTotal(CardColor.Green) == 1 &&
            stats.GetColorTotal(CardColor.Red) == 2 &&
            stats.GetColorTotal(CardColor.Blue) == 2,
            stats.GetColorTotal(CardColor.Yellow) + "/" +
            stats.GetColorTotal(CardColor.Green) + "/" +
            stats.GetColorTotal(CardColor.Red) + "/" +
            stats.GetColorTotal(CardColor.Blue));

        Check("deck stats leave 紫 out of the colour matrix",
            stats.GetColorTotal(CardColor.Purple) == 0);

        Check("deck stats cross colour with category",
            stats.GetColorCategoryTotal(CardColor.Yellow, CardCategory.Character) == 2 &&
            stats.GetColorCategoryTotal(CardColor.Yellow, CardCategory.Event) == 1 &&
            stats.GetColorCategoryTotal(CardColor.Green, CardCategory.Character) == 1 &&
            stats.GetColorCategoryTotal(CardColor.Red, CardCategory.Climax) == 1 &&
            stats.GetColorCategoryTotal(CardColor.Blue, CardCategory.Character) == 2,
            stats.GetColorCategoryTotal(CardColor.Yellow, CardCategory.Character) + "/" +
            stats.GetColorCategoryTotal(CardColor.Red, CardCategory.Climax));

        Check("deck stats histogram skips climax cards at level 0",
            stats.GetLevelTotal(0) == 4 &&
            stats.GetLevelTotal(1) == 2 &&
            stats.GetLevelTotal(2) == 1 &&
            stats.GetLevelTotal(3) == 1,
            stats.GetLevelTotal(0) + "/" + stats.GetLevelTotal(1) + "/" +
            stats.GetLevelTotal(2) + "/" + stats.GetLevelTotal(3));

        Check("deck stats count the 魂 and 2魂 trigger icons",
            stats.SoulTriggerCards == 5 && stats.DoubleSoulTriggerCards == 1,
            stats.SoulTriggerCards + "/" + stats.DoubleSoulTriggerCards);

        DeckStatistics empty = DeckStatistics.Compute(null, null);
        Check("an empty deck reports zero everywhere",
            empty.Total == 0 && empty.Unresolved == 0 &&
            empty.GetCategoryTotal(CardCategory.Character) == 0 &&
            empty.GetLevelTotal(0) == 0 &&
            empty.SoulTriggerCards == 0 && empty.DoubleSoulTriggerCards == 0);
    }

    private static void AddStatCard(Dictionary<string, WeissCard> cards,
        string id, CardCategory category, CardColor color, int level,
        TriggerIcon trigger)
    {
        cards[id] = new WeissCard
        {
            Id = id,
            Category = category,
            Color = color,
            Level = level,
            Trigger = trigger
        };
    }

    /// <summary>
    /// Image locator, card code folding and CommonEffects parameters; none of
    /// these need the game data.
    /// </summary>
    /// <summary>
    /// The in memory deck assembled by 编辑卡组: copy counts, the 50 card
    /// limit and the plain code list handed to the deck model.
    /// </summary>
    private static void TestDeckDraft()
    {
        var draft = new DeckDraft();
        Check("deck draft starts empty with the default name",
            draft.IsEmpty && draft.CardCount == 0 &&
            draft.Name == DeckDraft.DefaultName, draft.Name);

        Check("deck draft rejects a blank code",
            !draft.Add("  ") && draft.IsEmpty, draft.CardCount.ToString());
        Check("deck draft adds a card", draft.Add("MTI/S83-E055"),
            draft.CardCount.ToString());
        Check("deck draft repeats a code",
            draft.Add("mti/s83-e055") && draft.CardCount == 2 &&
            draft.Cards.Count == 1 && draft.CountOf("MTI/S83-E055") == 2,
            draft.CardCount + "/" + draft.Cards.Count);
        Check("deck draft removes one copy",
            draft.Remove("MTI/S83-E055") && draft.CardCount == 1,
            draft.CardCount.ToString());
        Check("deck draft drops the entry with its last copy",
            draft.Remove("MTI/S83-E055") && draft.IsEmpty,
            draft.Cards.Count.ToString());
        Check("deck draft ignores a missing removal",
            !draft.Remove("MTI/S83-E055"), "removed a card it never had");

        draft = new DeckDraft();
        for (int i = 0; i < DeckDraft.MaxCards; i++)
            draft.Add("CARD-" + (i % 10));
        Check("deck draft fills up to the 50 card limit",
            draft.CardCount == DeckDraft.MaxCards && draft.Cards.Count == 10,
            draft.CardCount + "/" + draft.Cards.Count);
        Check("deck draft allows cards past the 50 张 target",
            draft.Add("CARD-0") && draft.CardCount == DeckDraft.MaxCards + 1 &&
            draft.IsFull,
            draft.CardCount.ToString());

        IList<string> codes = draft.ToCardCodes();
        Check("deck draft lists every copy",
            codes.Count == DeckDraft.MaxCards + 1 && codes[0] == "CARD-0",
            codes.Count.ToString());

        draft.Sort();
        Check("deck draft sorts by card code",
            draft.Cards[0].Code == "CARD-0" && draft.Cards[9].Code == "CARD-9",
            draft.Cards[0].Code + ".." + draft.Cards[9].Code);

        draft.Clear();
        Check("deck draft clears but keeps its name",
            draft.IsEmpty && draft.Name == DeckDraft.DefaultName,
            draft.Name);
    }

    /// <summary>
    /// 保存 writes the tray into StreamingAssets\Decks. The file has to keep
    /// the plain code list the game already understands, and a name typed by
    /// the player has to survive as a readable file name.
    /// </summary>
    private static void TestDeckSaving()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-deck-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            var library = new DeckLibrary(root);
            var codes = new List<string>();
            for (int i = 0; i < 3; i++)
                codes.Add("MTI/S83-E101");

            string path;
            string error;
            Check("deck save needs a name",
                !library.SaveDeck("  ", codes, true, out path, out error),
                error ?? string.Empty);
            Check("deck save rejects an empty deck",
                !library.SaveDeck("空卡组", new List<string>(), true,
                    out path, out error),
                error ?? string.Empty);

            Check("deck save writes a local deck file",
                library.SaveDeck("测试 卡组", codes, false, out path, out error),
                error ?? string.Empty);
            Check("deck save uses the Decks folder",
                path != null && string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(path)),
                    DeckLibrary.LocalFolderName, StringComparison.OrdinalIgnoreCase),
                path ?? "missing");
            Check("deck save keeps the typed name",
                path != null &&
                Path.GetFileNameWithoutExtension(path) == "测试 卡组",
                Path.GetFileName(path ?? string.Empty));

            IList<string> written = path == null
                ? new List<string>()
                : DeckLibrary.ParseFile(path);
            Check("saved deck holds every copy",
                written.Count == 3 && written[0] == "MTI/S83-E101",
                written.Count.ToString());

            Check("deck save refuses to overwrite by default",
                !library.SaveDeck("测试 卡组", codes, false, out path, out error)
                && error == "已存在同名卡组",
                error ?? string.Empty);
            Check("deck save overwrites on demand",
                library.SaveDeck("测试 卡组", codes, true, out path, out error),
                error ?? string.Empty);
            Check("saved deck shows up in the library",
                library.LocalCount == 1, library.LocalCount.ToString());

            Check("deck save replaces illegal file name characters",
                DeckLibrary.GetSafeFileName("a/b:c*?") == "a_b_c__",
                DeckLibrary.GetSafeFileName("a/b:c*?"));
            Check("deck save keeps a usable file name",
                DeckLibrary.GetSafeFileName("   ") == string.Empty,
                DeckLibrary.GetSafeFileName("   "));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// 查看卡组 keeps 我的卡组 and 预设卡组 in two separate blocks. 删除 only
    /// touches the local block: the bundled decks are game files, and a tile
    /// whose file disappeared reports that instead of throwing.
    /// </summary>
    private static void TestDeckDelete()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-deck-delete-" + Guid.NewGuid().ToString("N"));
        try
        {
            string localRoot = Path.Combine(root, DeckLibrary.LocalFolderName);
            string defaultRoot = Path.Combine(root, DeckLibrary.DefaultFolderName);
            Directory.CreateDirectory(localRoot);
            Directory.CreateDirectory(defaultRoot);
            File.WriteAllText(Path.Combine(localRoot, "我的卡组.txt"),
                "MTI/S83-E101\r\nMTI/S83-E101\r\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(localRoot, "二副卡组.txt"),
                "MTI/S83-E025\r\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(defaultRoot, "预设卡组.txt"),
                "MTI/S83-E001\r\n", new UTF8Encoding(false));

            var library = DeckLibrary.Load(root);
            Check("deck blocks split local from default",
                library.LocalCount == 2 && library.DefaultCount == 1 &&
                library.Decks.Count == 3 &&
                library.LocalDecks.Count == 2 &&
                library.DefaultDecks.Count == 1 &&
                library.LocalDecks.All(d => d.Source == DeckSource.Local) &&
                library.DefaultDecks.All(d => d.Source == DeckSource.Default),
                library.LocalCount + "/" + library.DefaultCount);

            DeckDefinition local = library.LocalDecks[0];
            string localPath = local.FilePath;
            string error;
            Check("deck delete removes the local file",
                library.DeleteDeck(local, out error) && !File.Exists(localPath),
                error ?? localPath);
            Check("deck delete reloads the block counts",
                library.LocalCount == 1 && library.LocalDecks.Count == 1 &&
                library.DefaultCount == 1,
                library.LocalCount + "/" + library.DefaultCount);

            DeckDefinition bundled = library.DefaultDecks[0];
            string bundledPath = bundled.FilePath;
            Check("deck delete refuses a preset deck",
                !library.DeleteDeck(bundled, out error) &&
                error == "预设卡组不能删除" && File.Exists(bundledPath),
                error ?? string.Empty);

            var stale = new DeckDefinition("已移除",
                Path.Combine(localRoot, "已移除.txt"), DeckSource.Local,
                new[] { "MTI/S83-E101" });
            Check("deck delete reports a missing file",
                !library.DeleteDeck(stale, out error) &&
                error == "卡组文件已不存在",
                error ?? string.Empty);
            Check("deck delete ignores a null deck",
                !library.DeleteDeck(null, out error) && error == "卡组不存在",
                error ?? string.Empty);

            // Unity appends the djb2-xor hash to every PlayerPrefs value. These
            // pinned names come from a real game registry, so the builder can
            // remove Deck_/Date_/Sleeve_ values without decoding mojibake.
            Check("deck delete builds Unity PlayerPrefs value names",
                GameDeckRegistry.GetValueName("DeckNames") ==
                    "DeckNames_h3583298584" &&
                GameDeckRegistry.GetValueName("Deck_8yall") ==
                    "Deck_8yall_h2785203379" &&
                GameDeckRegistry.GetValueName("Deck_AI_5HY_8Door") ==
                    "Deck_AI_5HY_8Door_h712721009",
                GameDeckRegistry.GetValueName("Deck_AI_5HY_8Door"));
            // Chinese deck names are stored as ANSI mojibake, but the hash is
            // always over the original UTF-8 bytes. The visible part depends on
            // the code page, so only the suffix is pinned here.
            Check("deck delete hashes non-ASCII deck names like Unity",
                GameDeckRegistry.GetValueName("Deck_8Y黄图")
                    .EndsWith("_h961001252", StringComparison.Ordinal) &&
                GameDeckRegistry.GetValueName("Deck_8砖椅子王")
                    .EndsWith("_h1051351783", StringComparison.Ordinal) &&
                GameDeckRegistry.GetValueName("Deck_8砖椅子王2")
                    .EndsWith("_h334870517", StringComparison.Ordinal) &&
                GameDeckRegistry.GetValueName("Deck_门y彩龙")
                    .EndsWith("_h2354212963", StringComparison.Ordinal),
                GameDeckRegistry.GetValueName("Deck_8砖椅子王"));
            Check("deck delete removes a name from the game deck list",
                GameDeckRegistry.RemoveDeckName("A|B|C|", "B") == "A|C|" &&
                GameDeckRegistry.RemoveDeckName("A|B|", "B") == "A|" &&
                GameDeckRegistry.RemoveDeckName("A|", "missing") == "A|" &&
                GameDeckRegistry.RemoveDeckName("A|", "A") == string.Empty,
                GameDeckRegistry.RemoveDeckName("A|B|C|", "B"));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// 暂存 writes into StreamingAssets\DeckDrafts instead of Decks: five fixed
    /// slots, fixed file names, and a <c>#name</c> header that keeps the tray
    /// name without breaking the code list.
    /// </summary>
    private static void TestDeckDraftStore()
    {
        Check("staged deck file names are fixed",
            DeckDraftStore.GetSlotFileName(1) == "新建卡组1.txt" &&
            DeckDraftStore.GetSlotFileName(5) == "新建卡组5.txt",
            DeckDraftStore.GetSlotFileName(1) + "/" +
            DeckDraftStore.GetSlotFileName(5));
        Check("staged deck slots outside 1..5 have no file",
            DeckDraftStore.GetSlotFileName(0).Length == 0 &&
            DeckDraftStore.GetSlotFileName(6).Length == 0,
            DeckDraftStore.GetSlotFileName(6));
        Check("staged deck captions count the slots",
            DeckDraftStore.SlotCaption(2) == "暂存卡组 2",
            DeckDraftStore.SlotCaption(2));

        IList<string> pasted = DeckDraftStore.ParseCodes(
            "MTI/S83-E101\r\n\r\n  DAL/W79-E001  \n# comment\n// other\n" +
            "no-code\nMTI/S83-E101\n");
        Check("pasted deck code keeps the card lines",
            pasted.Count == 3 && pasted[0] == "MTI/S83-E101" &&
            pasted[1] == "DAL/W79-E001" && pasted[2] == "MTI/S83-E101",
            string.Join("|", pasted.ToArray()));
        Check("pasted deck code ignores blank input",
            DeckDraftStore.ParseCodes(null).Count == 0 &&
            DeckDraftStore.ParseCodes("   \n\n").Count == 0);

        var sourceCodes = new List<string> { "MTI/S83-E101", "MTI/S83-E101" };
        string content = DeckDraftStore.BuildFileContent("我的 卡组",
            sourceCodes);
        Check("staged deck file starts with the name header",
            content.StartsWith(DeckDraftStore.NameHeaderPrefix + "我的 卡组",
                StringComparison.Ordinal), content);

        string fileName;
        IList<string> parsedCodes;
        Check("staged deck file reads its name back",
            DeckDraftStore.TryParse(content, out fileName, out parsedCodes) &&
            fileName == "我的 卡组" && parsedCodes.Count == 2,
            fileName + "/" + parsedCodes.Count);
        Check("staged deck file without a header keeps the default name",
            DeckDraftStore.TryParse("MTI/S83-E101\n", out fileName,
                out parsedCodes) &&
            fileName == DeckDraft.DefaultName && parsedCodes.Count == 1,
            fileName);
        Check("deck library reads a staged file as a plain code list",
            DeckLibrary.ParseCardCodes(content.Split('\n')).Count == 2,
            DeckLibrary.ParseCardCodes(content.Split('\n')).Count.ToString());

        var partial = new List<DeckDraftSlot>();
        Check("first free staged slot starts at one",
            DeckDraftStore.FirstFreeSlot(partial) == 1 &&
            DeckDraftStore.FirstFreeSlot(null) == 1);
        partial.Add(new DeckDraftSlot(1, "A", "a", sourceCodes));
        partial.Add(new DeckDraftSlot(3, "B", "b", sourceCodes));
        Check("first free staged slot skips the taken ones",
            DeckDraftStore.FirstFreeSlot(partial) == 2,
            DeckDraftStore.FirstFreeSlot(partial).ToString());

        var full = new List<DeckDraftSlot>();
        for (int i = 1; i <= DeckDraftStore.SlotCount; i++)
            full.Add(new DeckDraftSlot(i, "A", "a", sourceCodes));
        Check("first free staged slot reports a full store",
            DeckDraftStore.FirstFreeSlot(full) == 0);

        TestDeckDraftStoreFiles();
    }

    /// <summary>
    /// The file side of 暂存: the folder sits next to Decks, the slot names are
    /// fixed, and a slot can be overwritten and deleted again.
    /// </summary>
    private static void TestDeckDraftStoreFiles()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-deck-draft-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DeckDraftStore(root);
            string slotPath = store.GetSlotPath(1);
            Check("staged deck lives in its own folder",
                string.Equals(Path.GetFileName(Path.GetDirectoryName(slotPath)),
                    DeckDraftStore.LocalFolderName,
                    StringComparison.OrdinalIgnoreCase),
                slotPath);
            Check("staged deck folder is not the deck folder",
                !string.Equals(Path.GetFileName(Path.GetDirectoryName(slotPath)),
                    DeckLibrary.LocalFolderName,
                    StringComparison.OrdinalIgnoreCase),
                slotPath);

            var codes = new List<string> { "MTI/S83-E101", "DAL/W79-E001" };
            string path;
            string error;
            Check("staged deck refuses an empty tray",
                !store.Save(1, "空卡组", new List<string>(), out path, out error),
                error ?? string.Empty);
            Check("staged deck refuses a slot outside 1..5",
                !store.Save(9, "卡组", codes, out path, out error),
                error ?? string.Empty);
            Check("staged deck saves into its slot",
                store.Save(1, "我的 卡组", codes, out path, out error),
                error ?? string.Empty);
            Check("staged deck writes the fixed file name",
                path != null &&
                Path.GetFileName(path) == DeckDraftStore.GetSlotFileName(1),
                Path.GetFileName(path ?? string.Empty));

            IList<DeckDraftSlot> slots = store.Load();
            Check("staged decks load back",
                slots.Count == 1 && slots[0].Index == 1 &&
                slots[0].DeckName == "我的 卡组" && slots[0].CardCount == 2,
                slots.Count.ToString());
            Check("staged deck keeps the next slot free",
                DeckDraftStore.FirstFreeSlot(slots) == 2,
                DeckDraftStore.FirstFreeSlot(slots).ToString());

            Check("staged deck overwrites its own slot",
                store.Save(1, "我的 卡组", new List<string> { "MTI/S83-E101" },
                    out path, out error) && store.Read(1).CardCount == 1,
                error ?? string.Empty);
            Check("staged deck ignores reading an empty slot",
                store.Read(4) == null);

            Check("staged deck deletes its file",
                store.Delete(1) && store.Load().Count == 0,
                store.Load().Count.ToString());
            Check("staged deck ignores deleting an empty slot",
                !store.Delete(4), "deleted a slot that was never used");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// 我的收藏 keeps one small file per card pack below MyCollocation. The
    /// file is sorted, ignores junk lines, clamps hand edits and a pack that
    /// lost every entry is deleted again, so the folder only holds real data.
    /// </summary>
    private static void TestCollectionStore()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-collection-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CollectionStore(root);
            Check("collection folder is MyCollocation",
                string.Equals(Path.GetFileName(store.Root),
                    CollectionStore.LocalFolderName, StringComparison.Ordinal),
                store.Root);
            Check("collection folder is created on load",
                Directory.Exists(store.Root), store.Root);
            Check("collection caps a card at 99 copies",
                CollectionStore.MaxCount == 99,
                CollectionStore.MaxCount.ToString());

            string packPath = store.GetPackPath("Accel World\\S18");
            Check("collection pack file derives from the folder path",
                packPath != null &&
                Path.GetFileName(packPath) == "Accel World_S18.txt",
                packPath ?? string.Empty);

            string path;
            string error;
            Check("collection refuses an empty pack path",
                !store.SavePack("  ", new Dictionary<string, int>(),
                    out path, out error),
                error ?? string.Empty);

            var counts = new Dictionary<string, int>();
            counts["aw/s18-e102"] = 2;
            counts["AW/S18-E101"] = 1;
            counts["AW/S18-E103"] = 0;
            counts[" "] = 5;
            counts["AW/S18-E104"] = 1200;
            Check("collection saves a pack",
                store.SavePack("Accel World/S18", counts,
                    out path, out error), error ?? string.Empty);
            Check("collection pack file sits in MyCollocation",
                path != null && string.Equals(Path.GetDirectoryName(path),
                    store.Root, StringComparison.OrdinalIgnoreCase),
                path ?? string.Empty);

            string text = File.ReadAllText(path);
            Check("collection file is sorted and normalised",
                text == "AW/S18-E101 1\r\nAW/S18-E102 2\r\nAW/S18-E104 99",
                text.Replace("\r\n", "|"));
            Check("collection clamps hand edited counts",
                store.GetCount("Accel World/S18", "aw/s18-e104") ==
                    CollectionStore.MaxCount,
                store.GetCount("Accel World/S18", "AW/S18-E104").ToString());
            Check("collection reads counts ignoring case",
                store.GetCount("Accel World/S18", "aw/s18-e102") == 2,
                store.GetCount("Accel World/S18", "AW/S18-E102").ToString());
            Check("collection ignores zero counts",
                store.GetCount("Accel World/S18", "AW/S18-E103") == 0,
                store.GetCount("Accel World/S18", "AW/S18-E103").ToString());

            int types;
            int copies;
            store.GetTotals(out types, out copies);
            Check("collection totals count the saved pack",
                types == 3 && copies == 102, types + "/" + copies);

            var parsed = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            CollectionStore.TryParsePack(
                "# comment\r\n\r\nAW/S18-E101 2\r\nbroken\r\n" +
                "AW/S18-E102 x\r\n// skip\r\n aw/s18-e103\t3 \r\n",
                parsed);
            Check("collection parser skips junk and comments",
                parsed.Count == 2 && parsed["AW/S18-E101"] == 2 &&
                parsed["AW/S18-E103"] == 3, parsed.Count.ToString());

            var series = new SeriesDefinition("Accel World");
            var folder = new SeriesFolder();
            folder.FolderPath = "Accel World/S18";
            series.Folders.Add(folder);
            Check("collection finds a series with owned cards",
                store.HasAnyOwned(series) &&
                store.GetSeriesOwnedTypes(series) == 3,
                store.GetSeriesOwnedTypes(series).ToString());

            Check("collection deletes an emptied pack",
                store.SavePack("Accel World/S18",
                    new Dictionary<string, int>(), out path, out error) &&
                !File.Exists(path), error ?? string.Empty);
            Check("collection reports nothing owned after the pack is gone",
                !store.HasAnyOwned(series) &&
                store.GetCount("Accel World/S18", "AW/S18-E101") == 0,
                store.GetCount("Accel World/S18", "AW/S18-E101").ToString());
            store.GetTotals(out types, out copies);
            Check("collection totals follow the deleted pack",
                types == 0 && copies == 0, types + "/" + copies);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// The 导入卡组 layer is checked off screen: reading a deck replaces the
    /// tray without a second question, a staged deck only disappears on the
    /// second right click (the first one paints its tile red) and Escape steps
    /// back through that red arm and the pasted block before it closes the
    /// layer. The layer is built on an STA thread without ever showing a
    /// window, which keeps the check free of the IME and focus problems that
    /// made the desktop run unreliable.
    /// </summary>
    private static void TestDeckImportOverlayEscape()
    {
        Exception error = null;
        var worker = new Thread(delegate()
        {
            try
            {
                TestDeckImportOverlayEscapeCore();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        worker.SetApartmentState(ApartmentState.STA);
        worker.IsBackground = true;
        worker.Start();
        worker.Join();
        if (error != null)
            Fail("import layer runs without a screen", error.ToString());
    }

    private static void TestDeckImportOverlayEscapeCore()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-import-escape-" + Guid.NewGuid().ToString("N"));
        var store = new DeckDraftStore(root);
        string savedPath;
        string saveError;
        bool staged = store.Save(1, "导入测试卡组",
            new[] { "MTI/S83-E101", "MTI/S83-E102" }, out savedPath,
            out saveError);
        Check("import layer stages a deck for the test", staged,
            saveError ?? savedPath);

        using (var thumbnails = new ThumbnailCache(4))
        using (var overlay = new DeckImportOverlay(store, null, thumbnails))
        {
            int closings = 0;
            int imports = 0;
            IList<string> importedCodes = null;
            overlay.CloseRequested += delegate { closings++; };
            overlay.ImportRequested +=
                delegate(object sender, DeckImportRequestEventArgs args)
                {
                    imports++;
                    importedCodes = args.CardCodes;
                };

            FieldInfo textPage = typeof(DeckImportOverlay).GetField(
                "_textPage", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo armedIndex = typeof(DeckImportOverlay).GetField(
                "_armedIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo armedSlot = typeof(DeckImportOverlay).GetField(
                "_armedSlot", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo tilesField = typeof(DeckImportOverlay).GetField(
                "_tiles", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo codeBoxField = typeof(DeckImportOverlay).GetField(
                "_codeBox", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo showTextPage = typeof(DeckImportOverlay).GetMethod(
                "ShowTextPage", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo acceptPressed = typeof(DeckImportOverlay).GetMethod(
                "AcceptPressed", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo tileClick = typeof(DeckImportOverlay).GetMethod(
                "Tiles_TileClick", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo tileRightClick = typeof(DeckImportOverlay).GetMethod(
                "Tiles_TileRightClick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Check("import layer state is reachable",
                textPage != null && armedIndex != null && armedSlot != null &&
                tilesField != null && codeBoxField != null &&
                showTextPage != null && acceptPressed != null &&
                tileClick != null && tileRightClick != null);
            if (textPage == null || armedIndex == null || armedSlot == null ||
                tilesField == null || codeBoxField == null ||
                showTextPage == null || acceptPressed == null ||
                tileClick == null || tileRightClick == null)
            {
                return;
            }

            overlay.ShowImport(null);
            Check("import layer opens on the tile page",
                !IsTextPage(textPage, overlay));
            overlay.HandleEscape();
            Check("escape closes the tile page in one press",
                closings == 1, "closes=" + closings);

            overlay.ShowImport(null);
            showTextPage.Invoke(overlay, null);
            Check("import layer shows the pasted block page",
                IsTextPage(textPage, overlay));
            overlay.HandleEscape();
            Check("escape steps back to the tile page first",
                closings == 1 && !IsTextPage(textPage, overlay),
                "closes=" + closings);
            overlay.HandleEscape();
            Check("escape closes the layer after the step back",
                closings == 2, "closes=" + closings);

            IList<DeckImportTile> tiles = GetImportTiles(tilesField, overlay);
            Check("staged deck shows up as a tile",
                tiles != null && tiles.Count == 2,
                "tiles=" + (tiles == null ? -1 : tiles.Count));
            if (tiles == null || tiles.Count != 2)
                return;

            tileRightClick.Invoke(overlay, new object[]
            {
                overlay, new DeckImportTileEventArgs(tiles[1], 1)
            });
            Check("first right click only arms the tile",
                store.Load().Count == 1 &&
                GetInt(armedIndex, overlay) == 1 &&
                GetInt(armedSlot, overlay) == 1 &&
                GetArmedTile(tilesField, overlay) == 1,
                "slots=" + store.Load().Count);

            overlay.HandleEscape();
            Check("escape disarms the tile instead of closing the layer",
                closings == 2 && GetInt(armedIndex, overlay) == -1 &&
                GetInt(armedSlot, overlay) == 0,
                "closes=" + closings);

            tileRightClick.Invoke(overlay, new object[]
            {
                overlay, new DeckImportTileEventArgs(tiles[1], 1)
            });
            tileClick.Invoke(overlay, new object[]
            {
                overlay, new DeckImportTileEventArgs(tiles[1], 1)
            });
            Check("left click on a red tile disarms instead of deleting",
                store.Load().Count == 1 && imports == 1 &&
                GetInt(armedIndex, overlay) == -1,
                "slots=" + store.Load().Count + " imports=" + imports);

            overlay.ShowImport(null);
            IList<DeckImportTile> reloaded = GetImportTiles(tilesField, overlay);
            if (reloaded != null && reloaded.Count == 2)
            {
                tileRightClick.Invoke(overlay, new object[]
                {
                    overlay, new DeckImportTileEventArgs(reloaded[1], 1)
                });
                tileRightClick.Invoke(overlay, new object[]
                {
                    overlay, new DeckImportTileEventArgs(reloaded[1], 1)
                });
            }

            Check("second right click deletes the staged deck",
                store.Load().Count == 0 && GetInt(armedIndex, overlay) == -1,
                "slots=" + store.Load().Count);
            Check("deleted tile leaves only the code cell",
                GetImportTiles(tilesField, overlay).Count == 1,
                "tiles=" + GetImportTiles(tilesField, overlay).Count);

            // A right click only reaches the layer after the grid turned the
            // mouse events into TileRightClick, so the delete gesture is
            // replayed through the control itself. Without this the handlers
            // above would still pass while the grid stayed silent on a tile
            // that has a cover.
            Check("import layer stages the deck again for the mouse test",
                store.Save(1, "导入测试卡组",
                    new[] { "MTI/S83-E101", "MTI/S83-E102" }, out savedPath,
                    out saveError),
                saveError ?? savedPath);
            overlay.ShowImport(null);

            var grid = tilesField.GetValue(overlay) as DeckImportTilesControl;
            Check("import layer hands the staged deck to the grid",
                grid != null && grid.Tiles.Count == 2,
                grid == null ? "grid=none" : "tiles=" + grid.Tiles.Count);
            if (grid != null && grid.Tiles.Count == 2)
            {
                // The layer never shows itself in the test, so the grid gets
                // the size a shown layer would have before it hit tests.
                grid.Width = 700;
                grid.Height = 400;

                Type gridType = grid.GetType();
                MethodInfo gridMouseDown = gridType.GetMethod("OnMouseDown",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo gridMouseUp = gridType.GetMethod("OnMouseUp",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo tileRectangle = gridType.GetMethod("GetTileRectangle",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Check("import grid exposes the mouse path of a tile",
                    gridMouseDown != null && gridMouseUp != null &&
                    tileRectangle != null);

                if (gridMouseDown != null && gridMouseUp != null &&
                    tileRectangle != null)
                {
                    var bounds = (System.Drawing.Rectangle)
                        tileRectangle.Invoke(grid, new object[] { 1 });
                    var centre = new System.Drawing.Point(
                        bounds.Left + bounds.Width / 2,
                        bounds.Top + bounds.Height / 2);
                    Action rightClick = delegate
                    {
                        gridMouseDown.Invoke(grid, new object[]
                        {
                            new System.Windows.Forms.MouseEventArgs(
                                System.Windows.Forms.MouseButtons.Right, 1,
                                centre.X, centre.Y, 0)
                        });
                        gridMouseUp.Invoke(grid, new object[]
                        {
                            new System.Windows.Forms.MouseEventArgs(
                                System.Windows.Forms.MouseButtons.Right, 1,
                                centre.X, centre.Y, 0)
                        });
                    };

                    rightClick();
                    Check("right click on a grid tile arms it",
                        store.Load().Count == 1 &&
                        GetArmedTile(tilesField, overlay) == 1,
                        "slots=" + store.Load().Count + " armed=" +
                        GetArmedTile(tilesField, overlay));

                    rightClick();
                    Check("second right click on the grid deletes the deck",
                        store.Load().Count == 0 &&
                        GetArmedTile(tilesField, overlay) == -1,
                        "slots=" + store.Load().Count + " armed=" +
                        GetArmedTile(tilesField, overlay));
                }
            }

            // A tray that already holds cards is replaced without a question.
            overlay.TrayCardCount = 5;
            overlay.ShowImport(null);
            object codeBox = codeBoxField.GetValue(overlay);
            codeBox.GetType().GetProperty("Text").SetValue(codeBox,
                "MTI/S83-E101" + Environment.NewLine + "MTI/S83-E102", null);
            acceptPressed.Invoke(overlay, null);
            Check("import replaces a non-empty tray without asking",
                imports == 2, "imports=" + imports);
            Check("import hands over the pasted codes",
                importedCodes != null && importedCodes.Count == 2 &&
                importedCodes[0] == "MTI/S83-E101",
                importedCodes == null ? "none" : importedCodes.Count.ToString());
        }
    }

    private static IList<DeckImportTile> GetImportTiles(FieldInfo tilesField,
        DeckImportOverlay overlay)
    {
        var control = tilesField.GetValue(overlay) as DeckImportTilesControl;
        return control == null ? null : control.Tiles;
    }

    /// <summary>Index of the red, armed tile the grid paints, -1 for none.</summary>
    private static int GetArmedTile(FieldInfo tilesField,
        DeckImportOverlay overlay)
    {
        var control = tilesField.GetValue(overlay) as DeckImportTilesControl;
        return control == null ? -1 : control.ArmedIndex;
    }

    private static int GetInt(FieldInfo field, DeckImportOverlay overlay)
    {
        object value = field.GetValue(overlay);
        return value is int ? (int)value : -1;
    }

    private static bool IsTextPage(FieldInfo textPage, DeckImportOverlay overlay)
    {
        object value = textPage.GetValue(overlay);
        return value is bool && (bool)value;
    }

    /// <summary>
    /// The deck detail exports write next to DeckBuilder instead of into the
    /// game data. The tests keep the file-name rules, the generated PNG and
    /// the PDF object layout together so one broken piece fails one line.
    /// </summary>
    private static void TestDeckExport()
    {
        Check("export file name replaces forbidden characters",
            DeckExportService.SanitizeFileName("测试/卡组:1? ") ==
            "测试_卡组_1_",
            DeckExportService.SanitizeFileName("测试/卡组:1? "));
        Check("export file name trims dots and spaces",
            DeckExportService.SanitizeFileName(" deck. ") == "deck" &&
            DeckExportService.SanitizeFileName("...") == "deck",
            DeckExportService.SanitizeFileName(" deck. "));
        Check("export file name protects reserved device names",
            DeckExportService.SanitizeFileName("CON") == "_CON" &&
            DeckExportService.SanitizeFileName("com1.txt") == "_com1.txt" &&
            DeckExportService.SanitizeFileName("NUL.deck") == "_NUL.deck",
            DeckExportService.SanitizeFileName("com1.txt"));

        string output = DeckExportService.GetOutputDirectory(null);
        Check("export output folder sits next to DeckBuilder",
            string.Equals(Path.GetFileName(output),
                DeckExportService.OutputFolderName,
                StringComparison.OrdinalIgnoreCase) &&
            output.IndexOf(Path.DirectorySeparatorChar + "DeckBuilder" +
                Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) < 0,
            output);

        var nameDeck = new DeckDefinition("导出/测试:卡组 ",
            string.Empty, DeckSource.Local, null);
        string pdfPath = DeckExportService.BuildExportPath(output, nameDeck,
            null, ".pdf");
        Check("export path combines a safe deck name with the export kind",
            Path.GetFileName(pdfPath) == "导出_测试_卡组.pdf",
            pdfPath);
        Check("export path uses the compact and expanded suffixes",
            Path.GetFileName(DeckExportService.BuildExportPath(output,
                nameDeck, "full", ".png")) == "导出_测试_卡组_full.png" &&
            Path.GetFileName(DeckExportService.BuildExportPath(output,
                nameDeck, "prev", ".png")) == "导出_测试_卡组_prev.png");
        Check("export path contains no forbidden characters",
            Path.GetFileName(pdfPath).IndexOfAny(
                Path.GetInvalidFileNameChars()) < 0,
            Path.GetFileName(pdfPath));

        var codeDeck = new DeckDefinition("代码测试", string.Empty,
            DeckSource.Local, new[]
            {
                "MTI/S83-E101",
                " ",
                null,
                " DAL/W79-E001 "
            });
        string codes = DeckExportService.BuildCardCodeText(codeDeck);
        Check("copied card codes keep one card per line",
            codes == "MTI/S83-E101\r\nDAL/W79-E001",
            codes.Replace("\r", "\\r").Replace("\n", "\\n"));
        Check("an empty deck copies no card codes",
            DeckExportService.BuildCardCodeText(null) == string.Empty &&
            DeckExportService.BuildCardCodeText(new DeckDefinition(
                "空", string.Empty, DeckSource.Local, null)) == string.Empty);

        string pdf = Path.Combine(Path.GetTempPath(),
            "ws-deck-export-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            var image = new DeckPdfImage(
                new byte[] { 0xFF, 0xD8, 0xFF, 0xD9, 0xFF, 0xD9 }, 1, 1);
            var first = new List<DeckPdfImage>();
            for (int i = 0; i < DeckPdfWriter.CardsPerPage; i++)
                first.Add(image);
            var second = new List<DeckPdfImage> { image };
            var pages = new List<IList<DeckPdfImage>> { first, second };
            DeckPdfWriter.Write(pdf, pages,
                DeckExportService.WatermarkText);

            string text = Encoding.ASCII.GetString(File.ReadAllBytes(pdf));
            Check("deck PDF starts with a PDF header",
                text.StartsWith("%PDF-1.4", StringComparison.Ordinal));
            Check("deck PDF writes one page object per page",
                CountOccurrences(text, "/Type /Page /Parent") == 2,
                CountOccurrences(text, "/Type /Page /Parent").ToString());
            Check("deck PDF counts both pages in the page tree",
                text.Contains("/Type /Pages /Kids [3 0 R 14 0 R] /Count 2"),
                text.Contains("/Count") ? "count mismatch" : "no count");
            Check("deck PDF embeds JPEG images with DCTDecode",
                CountOccurrences(text, "/Filter /DCTDecode") == 10,
                CountOccurrences(text, "/Filter /DCTDecode").ToString());
            Check("deck PDF ends with a cross-reference trailer",
                text.Contains("xref") && text.Contains("startxref") &&
                text.TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal));
            Check("deck PDF carries the author watermark",
                DeckExportService.WatermarkText ==
                    "Weiss Schwarz Deck Builder Made by xineist" &&
                text.Contains("/BaseFont /Helvetica") &&
                text.Contains("(Weiss Schwarz Deck Builder Made by xineist) Tj"),
                DeckExportService.WatermarkText);

            bool emptyRejected = false;
            try
            {
                DeckPdfWriter.Write(string.Empty, pages);
            }
            catch (ArgumentException)
            {
                emptyRejected = true;
            }
            Check("deck PDF rejects an empty path", emptyRejected);

            bool overflowRejected = false;
            var tooMany = new List<DeckPdfImage>();
            for (int i = 0; i <= DeckPdfWriter.CardsPerPage; i++)
                tooMany.Add(image);
            try
            {
                DeckPdfWriter.Write(pdf + ".overflow",
                    new List<IList<DeckPdfImage>> { tooMany });
            }
            catch (InvalidOperationException)
            {
                overflowRejected = true;
            }
            Check("deck PDF rejects more than nine cards on a page",
                overflowRejected);
        }
        finally
        {
            if (File.Exists(pdf))
                File.Delete(pdf);
            if (File.Exists(pdf + ".overflow"))
                File.Delete(pdf + ".overflow");
        }

        string deckName = "导出测试" + Guid.NewGuid().ToString("N");
        var exportDeck = new DeckDefinition(deckName, string.Empty,
            DeckSource.Local, new[]
            {
                "MTI/S83-E101",
                "DAL/W79-E001",
                "MTI/S83-E101"
            });
        DeckExportResult expanded = null;
        DeckExportResult compact = null;
        try
        {
            IList<string> ordered = DeckComposition.OrderByCode(
                exportDeck.CardCodes);
            expanded = DeckExportService.ExportExpandedPng(exportDeck, ordered,
                new string[ordered.Count], "展开 · 测试");

            IList<DeckCardCount> groups = DeckComposition.GroupByCode(
                exportDeck.CardCodes);
            compact = DeckExportService.ExportCompactPng(exportDeck, groups,
                new string[groups.Count], "缩略 · 测试");

            Check("expanded deck sheet is written as PNG",
                expanded.Success && File.Exists(expanded.FilePath) &&
                Path.GetFileName(expanded.FilePath) ==
                    DeckExportService.SanitizeFileName(deckName) +
                    "_full.png",
                expanded.Success ? expanded.FilePath : expanded.Error);
            Check("compact deck sheet is written as PNG",
                compact.Success && File.Exists(compact.FilePath) &&
                Path.GetFileName(compact.FilePath) ==
                    DeckExportService.SanitizeFileName(deckName) +
                    "_prev.png",
                compact.Success ? compact.FilePath : compact.Error);

            byte[] header = expanded.Success
                ? ReadHeader(expanded.FilePath, 8)
                : new byte[0];
            byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            bool signature = header.Length == png.Length;
            for (int i = 0; signature && i < png.Length; i++)
                signature = header[i] == png[i];
            Check("deck PNG starts with the PNG signature", signature);
        }
        finally
        {
            if (expanded != null && expanded.Success &&
                File.Exists(expanded.FilePath))
            {
                File.Delete(expanded.FilePath);
            }
            if (compact != null && compact.Success &&
                File.Exists(compact.FilePath))
            {
                File.Delete(compact.FilePath);
            }
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
            return 0;

        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index,
            StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static byte[] ReadHeader(string path, int count)
    {
        var buffer = new byte[count];
        using (var stream = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.Read))
        {
            int read = 0;
            while (read < count)
            {
                int step = stream.Read(buffer, read, count - read);
                if (step <= 0)
                    break;
                read += step;
            }
        }
        return buffer;
    }

    private static void TestCardCodeHelpers()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "HOL_W104_E041", "hol-041.jpg" }
        };

        var card = new WeissCard();
        card.Id = "HOL/W104-041";
        card.ImageId = "HOL_W104_041";
        ImageLocator.Resolve(card, index);
        Check("image locator adds the parallel E",
            card.ImagePath == "hol-041.jpg", card.ImagePath ?? "missing");

        string path = Path.Combine(Path.GetTempPath(), "ws-smoke-effects.txt");
        File.WriteAllText(path,
            "Define: Bond1(CARDNAME)\r\n" +
            "\tText Add $CARDNAME now\r\n" +
            "Define: Revenge(POWER)\r\n" +
            "\tText Power +POWER .\r\n",
            new UTF8Encoding(false));

        CommonEffectLibrary library = CommonEffectLibrary.Load(path);
        CommonEffectDefinition bond = library.Find("Bond1(Actress Cafe\\, Ayaka)");
        Check("effect parameter keeps an escaped comma",
            bond != null && bond.TextLines.Count == 1 &&
            bond.TextLines[0].Contains("Actress Cafe, Ayaka"),
            bond == null ? "no definition" : string.Join("|", bond.TextLines.ToArray()));

        CommonEffectDefinition revenge = library.Find("Revenge(1000)");
        Check("effect replaces a bare parameter",
            revenge != null && revenge.TextLines.Count == 1 &&
            revenge.TextLines[0].Contains("+1000"),
            revenge == null ? "no definition" : string.Join("|", revenge.TextLines.ToArray()));

        CommonEffectDefinition commented =
            library.Find("Revenge(2000) // note");
        Check("effect name ignores a trailing comment",
            commented != null && commented.TextLines.Count == 1 &&
            commented.TextLines[0].Contains("+2000"),
            commented == null ? "no definition" : string.Join("|", commented.TextLines.ToArray()));

        File.Delete(path);
    }

    /// <summary>
    /// Covers the deck-sync plugin's pure helpers: the game's PlayerPrefs text
    /// layout ("CODE|CODE|", "HH:mm  MM/dd/yyyy"), the card code guard and the
    /// "which side changed" rule the plugin uses to pick a winner.
    /// </summary>
    private static void TestDeckSyncPlanner()
    {
        var ids = new List<string> { "DAL/W79-E001", "DAL/W79-E001", "5HY/W101-E040" };
        string deckText = DeckSyncPlanner.ToDeckText(ids);
        Check("deck sync writes the game deck layout",
            deckText == "DAL/W79-E001|DAL/W79-E001|5HY/W101-E040|", deckText);

        IList<string> roundTrip = DeckSyncPlanner.SplitDeckText(deckText);
        Check("deck sync reads the game deck layout back",
            roundTrip.Count == 3 && roundTrip[2] == "5HY/W101-E040",
            roundTrip.Count.ToString());

        IList<string> parsed = DeckSyncPlanner.ParseCardLines(
            new[] { "", "   ", "# comment", " DAL/W79-E001 ", "DAL/W79-E002" });
        Check("deck sync drops blank lines and comments",
            parsed.Count == 2 && parsed[0] == "DAL/W79-E001",
            string.Join("|", parsed.ToArray()));

        Check("deck sync accepts real card codes",
            DeckSyncPlanner.IsCardCode("DAL/W79-E053SEC") &&
            DeckSyncPlanner.IsCardCode("5HY/W101-E040") &&
            DeckSyncPlanner.IsCardCode("GL/S52-T03"));
        Check("deck sync rejects text that is not a card code",
            !DeckSyncPlanner.IsCardCode(string.Empty) &&
            !DeckSyncPlanner.IsCardCode("DAL/W79 E001") &&
            !DeckSyncPlanner.IsCardCode("A/B/C") &&
            !DeckSyncPlanner.IsCardCode("DALW79E001"));

        var stamp = new DateTime(2026, 11, 13, 3, 42, 0);
        string dateText = DeckSyncPlanner.FormatDate(stamp);
        Check("deck sync writes the game save date",
            dateText == "03:42  11/13/2026", dateText);

        DateTime readBack;
        Check("deck sync parses the game save date",
            DeckSyncPlanner.TryParseDate(dateText, out readBack) && readBack == stamp,
            dateText);
        Check("deck sync rejects an unreadable save date",
            !DeckSyncPlanner.TryParseDate("yesterday", out readBack));

        Check("deck sync hash is stable and content based",
            DeckSyncPlanner.Hash("DAL/W79-E001|") == DeckSyncPlanner.Hash("DAL/W79-E001|") &&
            DeckSyncPlanner.Hash("DAL/W79-E001|") != DeckSyncPlanner.Hash("DAL/W79-E002|"));

        Check("deck sync appends a deck name in the game layout",
            DeckSyncPlanner.AppendDeckName("AI_Test|8Y|", "New") == "AI_Test|8Y|New|",
            DeckSyncPlanner.AppendDeckName("AI_Test|8Y|", "New"));
        Check("deck sync keeps an already listed deck name",
            DeckSyncPlanner.AppendDeckName("AI_Test|8Y|", "8Y") == "AI_Test|8Y|");
        Check("deck sync repairs an unterminated deck list",
            DeckSyncPlanner.AppendDeckName("A|B", "C") == "A|B|C|",
            DeckSyncPlanner.AppendDeckName("A|B", "C"));
        Check("deck sync starts an empty deck list",
            DeckSyncPlanner.AppendDeckName(string.Empty, "C") == "C|",
            DeckSyncPlanner.AppendDeckName(string.Empty, "C"));
        Check("deck sync removes a name from the game list",
            DeckSyncPlanner.RemoveDeckName("A|B|C|", "B") == "A|C|" &&
            DeckSyncPlanner.RemoveDeckName("A|B|", "B") == "A|" &&
            DeckSyncPlanner.RemoveDeckName("A|", "missing") == "A|" &&
            DeckSyncPlanner.RemoveDeckName("A|", "A") == string.Empty);

        TestDeckSyncConflict();
    }

    /// <summary>
    /// Decides which side wins: whichever changed since the last run, or the
    /// newer save time when both moved.
    /// </summary>
    private static void TestDeckSyncConflict()
    {
        const string local = "DAL/W79-E001|";
        const string remote = "DAL/W79-E002|";
        var earlier = new DateTime(2026, 11, 13, 9, 0, 0);
        var later = new DateTime(2026, 11, 13, 12, 0, 0);
        string localHash = DeckSyncPlanner.Hash(local);
        string remoteHash = DeckSyncPlanner.Hash(remote);

        Check("deck sync imports a new local deck",
            DeckSyncPlanner.Decide(local, null, null, null, earlier, null) ==
            DeckSyncAction.PushToGame);
        Check("deck sync exports a new game deck",
            DeckSyncPlanner.Decide(null, remote, null, null, null, later) ==
            DeckSyncAction.PullToFile);
        Check("deck sync ignores identical content",
            DeckSyncPlanner.Decide(local, local, "a", "b", earlier, later) ==
            DeckSyncAction.None);
        Check("deck sync ignores a deck missing on both sides",
            DeckSyncPlanner.Decide(null, null, null, null, null, null) ==
            DeckSyncAction.None);
        Check("deck sync imports the edited file",
            DeckSyncPlanner.Decide(local, remote, remoteHash, remoteHash, earlier, later) ==
            DeckSyncAction.PushToGame);
        Check("deck sync exports the edited game deck",
            DeckSyncPlanner.Decide(local, remote, localHash, localHash, later, earlier) ==
            DeckSyncAction.PullToFile);
        Check("deck sync lets the newer save time win",
            DeckSyncPlanner.Decide(local, remote, null, null, later, earlier) ==
            DeckSyncAction.PushToGame &&
            DeckSyncPlanner.Decide(local, remote, null, null, earlier, later) ==
            DeckSyncAction.PullToFile);

        Check("deck sync detects a deleted mirror file",
            DeckSyncPlanner.IsFileSideDeleted(
                null, remote, localHash, remoteHash) &&
            !DeckSyncPlanner.IsFileSideDeleted(
                null, remote, localHash, localHash) &&
            !DeckSyncPlanner.IsFileSideDeleted(
                local, remote, localHash, remoteHash));

        TestDeckSyncState();
    }

    private static void TestDeckSyncState()
    {
        string path = Path.Combine(Path.GetTempPath(), "ws-deck-sync-state.txt");
        if (File.Exists(path))
            File.Delete(path);

        DeckSyncState state = DeckSyncState.Load(path);
        Check("deck sync state starts empty",
            state.Find("8Y黄图") == null && state.Names.Count == 0);

        state.Set("8Y黄图", "aaaa", "bbbb");
        state.Set("New Deck", "cccc", "dddd");
        state.Save(path);

        DeckSyncState reloaded = DeckSyncState.Load(path);
        DeckSyncEntry entry = reloaded.Find("8Y黄图");
        Check("deck sync state keeps both hashes",
            entry != null && entry.FileHash == "aaaa" && entry.GameHash == "bbbb",
            entry == null ? "missing" : entry.FileHash + "/" + entry.GameHash);
        Check("deck sync state lists every deck", reloaded.Names.Count == 2,
            reloaded.Names.Count.ToString());

        reloaded.Remove("New Deck");
        reloaded.Save(path);
        Check("deck sync state forgets removed decks",
            DeckSyncState.Load(path).Names.Count == 1,
            DeckSyncState.Load(path).Names.Count.ToString());

        File.Delete(path);
    }

    /// <summary>
    /// Each fixture is a real archive generated by 7-Zip / WinRAR from the same
    /// source tree, so this exercises the production ZIP path plus the vendored
    /// SharpCompress path. The solid 7z and solid RAR fixtures validate the
    /// sequential decompression path; the RAR checks are skipped when those
    /// fixtures are absent.
    /// </summary>
    private static void TestCardPackArchives()
    {
        // SmokeTests.exe lives in tests\dist, so the fixtures sit two levels
        // up under tests\fixtures.
        string fixtures = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "tests", "fixtures"));

        TestCardPackArchive(fixtures, "cardpack.zip");
        TestCardPackArchive(fixtures, "cardpack.7z");
        TestCardPackArchive(fixtures, "cardpack.solid.7z");

        foreach (string rarName in new[] { "cardpack.rar", "cardpack.solid.rar" })
        {
            if (File.Exists(Path.Combine(fixtures, rarName)))
            {
                TestCardPackArchive(fixtures, rarName);
            }
            else
            {
                Log.AppendLine("SKIP: " + rarName + " fixture is missing; " +
                    "RAR support for that variant was not exercised.");
            }
        }

        TestCardPackSupplement();
    }

    /// <summary>
    /// A supplement pack may contain only images or CommonEffects for a series
    /// that is already installed. It must be accepted without SingleSetData or
    /// CardData; when metadata is present it is still used for the summary.
    /// </summary>
    private static void TestCardPackSupplement()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-cardpack-supplement-" + Guid.NewGuid().ToString("N"));
        string dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(dataRoot);
        try
        {
            string imageOnlyPath = Path.Combine(root, "images-only.zip");
            using (FileStream stream = File.Create(imageOnlyPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                AddZipEntry(archive,
                    "Weiss Schwarz_Data/StreamingAssets/Cards/Supplement/S01/001.png",
                    new byte[] { 1, 2, 3 });
                AddZipEntry(archive,
                    "Weiss Schwarz_Data/StreamingAssets/Cards/Supplement/CommonEffects.txt",
                    "Define: Test\r\n");
                AddZipEntry(archive,
                    "Weiss Schwarz_Data/StreamingAssets/AlternateArtwork/Supplement/001a.jpg",
                    new byte[] { 4, 5, 6 });
            }

            CardPackInstallResult imageOnly =
                CardPackInstaller.Install(imageOnlyPath, dataRoot);
            Check("cardpack supplement installs without metadata",
                imageOnly.Success, imageOnly.ErrorMessage);
            Check("cardpack supplement reports no series",
                imageOnly.SeriesCount == 0 && string.IsNullOrEmpty(imageOnly.SeriesName),
                imageOnly.SeriesCount + "/" + imageOnly.SeriesName);
            Check("cardpack supplement extracts every file",
                imageOnly.ExtractedFileCount == 3,
                imageOnly.ExtractedFileCount.ToString());
            Check("cardpack supplement extracts the card image",
                File.Exists(Path.Combine(
                    dataRoot, "Cards", "Supplement", "S01", "001.png")));
            Check("cardpack supplement extracts the effect file",
                File.Exists(Path.Combine(
                    dataRoot, "Cards", "Supplement", "CommonEffects.txt")));
            Check("cardpack supplement extracts alternate artwork",
                File.Exists(Path.Combine(
                    dataRoot, "AlternateArtwork", "Supplement", "001a.jpg")));

            string metadataPath = Path.Combine(root, "metadata-supplement.zip");
            using (FileStream stream = File.Create(metadataPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                AddZipEntry(archive,
                    "Weiss Schwarz_Data/StreamingAssets/Cards/Supplement/SingleSetData.txt",
                    "Series: Supplement\r\nFolder: Supplement/S01\r\nPrefix: SP/S01\r\n");
                AddZipEntry(archive,
                    "Weiss Schwarz_Data/StreamingAssets/Cards/Supplement/S01/002.png",
                    new byte[] { 7, 8, 9 });
            }

            CardPackInstallResult metadataOnly =
                CardPackInstaller.Install(metadataPath, dataRoot);
            Check("cardpack supplement accepts metadata without CardData",
                metadataOnly.Success, metadataOnly.ErrorMessage);
            Check("cardpack supplement keeps metadata series name",
                metadataOnly.SeriesName == "Supplement" &&
                metadataOnly.SeriesCount == 1,
                metadataOnly.SeriesCount + "/" + metadataOnly.SeriesName);
            Check("cardpack supplement reports zero cards for image-only metadata",
                metadataOnly.CardCount == 0,
                metadataOnly.CardCount.ToString());
            Check("cardpack supplement extracts the metadata image",
                File.Exists(Path.Combine(
                    dataRoot, "Cards", "Supplement", "S01", "002.png")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void AddZipEntry(ZipArchive archive, string name, byte[] content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using (Stream output = entry.Open())
            output.Write(content, 0, content.Length);
    }

    private static void AddZipEntry(ZipArchive archive, string name, string content)
    {
        AddZipEntry(archive, name, Encoding.UTF8.GetBytes(content));
    }

    private static void TestCardPackArchive(string fixtures, string fileName)
    {
        string archivePath = Path.Combine(fixtures, fileName);
        if (!File.Exists(archivePath))
        {
            Fail("cardpack fixture " + fileName + " exists", archivePath);
            return;
        }

        string dataRoot = Path.Combine(Path.GetTempPath(),
            "ws-cardpack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        try
        {
            CardPackInstallResult result =
                CardPackInstaller.Install(archivePath, dataRoot);
            Check("cardpack " + fileName + " installs", result.Success,
                result.ErrorMessage);
            if (!result.Success)
                return;

            Check("cardpack " + fileName + " keeps the Chinese series name",
                result.SeriesName == "压缩测试", result.SeriesName);
            Check("cardpack " + fileName + " counts the card",
                result.CardCount == 1, result.CardCount.ToString());

            string seriesRoot = Path.Combine(
                dataRoot, "Cards", "压缩测试");
            Check("cardpack " + fileName + " extracts SingleSetData",
                File.Exists(Path.Combine(seriesRoot, "SingleSetData.txt")));
            Check("cardpack " + fileName + " extracts CardData",
                File.Exists(Path.Combine(seriesRoot, "S01", "CardData.txt")));
            Check("cardpack " + fileName + " extracts the card image",
                File.Exists(Path.Combine(seriesRoot, "S01", "ZC_S01_001.png")));

            string cardData = Path.Combine(seriesRoot, "S01", "CardData.txt");
            string text = File.Exists(cardData)
                ? File.ReadAllText(cardData, Encoding.UTF8)
                : string.Empty;
            Check("cardpack " + fileName + " round-trips the card text",
                text.IndexOf("ZC/S01-001", StringComparison.Ordinal) >= 0);
        }
        finally
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, true);
        }
    }

    private static WeissCard MakeCard(string id, string name)
    {
        return new WeissCard { Id = id, Name = name };
    }

    private static void Check(string name, bool condition, string detail = null)
    {
        if (condition)
        {
            _passed++;
            Log.AppendLine("PASS: " + name);
        }
        else
        {
            _failed++;
            Log.AppendLine("FAIL: " + name +
                (string.IsNullOrEmpty(detail) ? string.Empty : " [" + detail + "]"));
        }
    }

    private static void Fail(string name, string detail)
    {
        Check(name, false, detail);
    }

    private static bool HasFlag(string[] args, string flag)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Reads <c>--only a,b</c> (or <c>--group a,b</c>); run-tests.ps1 passes
    /// <c>-Group</c> through as this option. No filter means "all".
    /// </summary>
    private static string[] ReadGroupFilter(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if ((string.Equals(args[i], "--only", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[i], "--group", StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < args.Length)
            {
                return args[i + 1].Split(
                    new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            }
        }
        return null;
    }

    private static List<TestGroup> SelectGroups(
        List<TestGroup> groups, string[] requested)
    {
        if (requested == null || requested.Length == 0)
            return groups;

        var selected = new List<TestGroup>();
        foreach (string raw in requested)
        {
            string key = raw.Trim();
            if (key.Length == 0)
                continue;
            if (string.Equals(key, "all", StringComparison.OrdinalIgnoreCase))
                return groups;

            TestGroup match = groups.FirstOrDefault(
                g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                Fail("group " + key + " exists",
                    "可用分组: " + string.Join(", ",
                        groups.Select(g => g.Key).ToArray()));
                continue;
            }
            if (!selected.Contains(match))
                selected.Add(match);
        }
        return selected;
    }

    private static string ReadLogPath(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--log", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "test-results.txt");
    }

    private static int Finish(string logPath, string summary)
    {
        Log.AppendLine();
        if (!string.IsNullOrEmpty(summary))
            Log.AppendLine(summary);
        Log.AppendLine(string.Format("RESULT: {0} passed, {1} failed", _passed, _failed));
        string text = Log.ToString();
        Console.WriteLine(text);

        try
        {
            string directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(logPath, text, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not write log: " + ex.Message);
        }

        return _failed == 0 ? 0 : 1;
    }
}
