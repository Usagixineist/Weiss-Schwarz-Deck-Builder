using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
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
                db => TestCollectionStore()),
            new TestGroup("glossary", "触发名称：术语表与三个解析入口", false,
                db => TestTriggerGlossary()),
            new TestGroup("theme", "外观：主题设置与配色表", false,
                db => { TestSettingsFile(); TestAppSettings(); TestAppPalette(); TestThemeRecolor(); }),
            new TestGroup("cardedit", "编辑卡片：卡块生成、写回与编辑器", false,
                db => { TestCardEditor(); TestCardEditorPage(); TestCardImageDrops();
                    TestCardEffectPairs(); TestCardEditorShorthandText();
                    TestCardEditorShorthandPairs();
                    TestCardEditorShorthandRow();
                    TestCardEditorClimaxShorthand();
                    TestCardEditorCloneBlock(); }),
            new TestGroup("packindex", "添加卡片：卡号定位卡包", false,
                db => TestCardPackIndex()),
            new TestGroup("images", "图片来源：一个目录只扫一次", false,
                db => TestImageIndex()),
            new TestGroup("altart", "添加异画：命名规则、写入与识别", false,
                db => { TestAltArtNaming(); TestAltArtWriter(); TestAltArtPage(); }),
            new TestGroup("launcher", "启动游戏：定位游戏目录与不热更参数", true,
                db => TestGameLauncher(db))
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

            // JPNames.txt supplies the display name. LoadFolder applies it once,
            // after the extra rarities were appended, so a card and the alt art
            // clone that copies it both end up with the same Japanese name.
            Check("E002 keeps its Japanese name",
                !string.IsNullOrEmpty(e002.JapaneseName),
                e002.JapaneseName ?? "missing");
            Check("E002X shares the Japanese name of E002",
                e002x.JapaneseName == e002.JapaneseName,
                e002x.JapaneseName ?? "missing");
        }

        Check("no empty card names", s18.Cards.All(c => !string.IsNullOrEmpty(c.Name)));

        TestRealEffectPairs(series);
        TestRealCloneBlocks(series);
        TestRealShorthandBlocks(series);

        // 添加卡片 resolves the card pack from the 卡号 alone, so the index has
        // to find the real folder (and it has to exist on disk).
        CardPackIndex packs = CardPackIndex.Build(database);
        Check("pack index covers every card pack", packs.Count > 400,
            packs.Count.ToString());
        CardPackTarget awPack = packs.Resolve("AW/S18-E101");
        Check("pack index resolves AW/S18 to the 加速世界 pack",
            awPack != null && awPack.SeriesName == "加速世界" &&
            awPack.Folder.FolderPath.Replace('\\', '/').EndsWith("Accel World/S18",
                StringComparison.OrdinalIgnoreCase) &&
            awPack.SetName.Contains("S18"),
            awPack == null ? "missing" : awPack.Folder.FolderPath + " / " + awPack.SetName);
        Check("pack index resolves to an existing folder",
            awPack != null && Directory.Exists(awPack.FolderFullPath),
            awPack == null ? "missing" : awPack.FolderFullPath);

        IList<CardPackTarget> ksrPacks = packs.Find("KSR/01B-001");
        Check("pack index keeps a shared promo prefix ambiguous",
            ksrPacks.Count == 2 && packs.Resolve("KSR/01B-001") == null,
            ksrPacks.Count.ToString());
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

        Check("deck stats count every trigger icon",
            stats.GetTriggerTotal(TriggerIcon.Soul) == 5 &&
            stats.GetTriggerTotal(TriggerIcon.DoubleSoul) == 1 &&
            stats.GetTriggerTotal(TriggerIcon.Book) == 0 &&
            stats.GetTriggerTotal(TriggerIcon.None) == 0,
            stats.GetTriggerTotal(TriggerIcon.Soul) + "/" +
            stats.GetTriggerTotal(TriggerIcon.DoubleSoul) + "/" +
            stats.GetTriggerTotal(TriggerIcon.Book));

        IList<TriggerIcon> used = stats.GetTriggersInUse();
        Check("deck stats list only the triggers the deck uses",
            used.Count == 2 && used[0] == TriggerIcon.Soul &&
            used[1] == TriggerIcon.DoubleSoul,
            used.Count.ToString());

        DeckStatistics empty = DeckStatistics.Compute(null, null);
        Check("an empty deck reports zero everywhere",
            empty.Total == 0 && empty.Unresolved == 0 &&
            empty.GetCategoryTotal(CardCategory.Character) == 0 &&
            empty.GetLevelTotal(0) == 0 &&
            empty.SoulTriggerCards == 0 && empty.DoubleSoulTriggerCards == 0 &&
            empty.GetTriggersInUse().Count == 0);
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
    /// 一个临时目录里唯一的设置文件。主题和触发名称现在共用它，所以测试只需要
    /// 一个路径就能同时读两边。
    /// </summary>
    private static SettingsStore SettingsFor(string root)
    {
        return new SettingsStore(Path.Combine(root, SettingsStore.FileName));
    }

    /// <summary>
    /// 设置页的所有改动只落一个文件：主题在 [appearance]、触发名称在
    /// [terminology]，两边各存一次互不覆盖；这个版本不认识的分组和键原样带回；
    /// 旧版本留下的 StreamingAssets\AppSettings.txt / TriggerGlossary.txt 在第一次
    /// 启动时搬进新文件再删掉，用户改过的主题和名字不会丢。
    /// </summary>
    private static void TestSettingsFile()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-settingsfile-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, SettingsStore.FileName);
        try
        {
            SettingsStore store = SettingsFor(root);
            var settings = new AppSettings(store);
            var glossary = new TriggerGlossary(store);
            Check("settings and glossary share one file",
                string.Equals(settings.FilePath, path,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(glossary.FilePath, path,
                    StringComparison.OrdinalIgnoreCase),
                settings.FilePath + " / " + glossary.FilePath);

            string error;
            settings.Theme = AppThemeMode.Light;
            Check("the one file stores the theme",
                settings.Save(out error), error ?? string.Empty);
            glossary.SetName(TriggerIcon.Book, "书");
            Check("the one file stores the trigger name",
                glossary.Save(out error), error ?? string.Empty);

            string text = File.ReadAllText(path);
            Check("the one file holds both groups",
                text.Contains("[appearance]") && text.Contains("theme=light") &&
                text.Contains("[terminology]") && text.Contains("Book=书"), text);

            var reopenedStore = SettingsFor(root);
            Check("one file brings the theme back",
                new AppSettings(reopenedStore).Theme == AppThemeMode.Light,
                AppSettings.ToText(new AppSettings(reopenedStore).Theme));
            Check("one file brings the trigger name back",
                new TriggerGlossary(reopenedStore).GetName(TriggerIcon.Book) == "书",
                new TriggerGlossary(reopenedStore).GetName(TriggerIcon.Book));

            // 新版本写的分组，旧版本读到时不能抹掉，否则升级/降级一次就少一项设置。
            File.WriteAllText(path,
                "# note\r\n\r\n[appearance]\r\ntheme=dark\r\n\r\n[future]\r\nkey=1\r\n",
                new UTF8Encoding(false));
            SettingsStore futureStore = SettingsFor(root);
            var futureSettings = new AppSettings(futureStore);
            futureSettings.Theme = AppThemeMode.Light;
            Check("a save keeps unknown groups",
                futureSettings.Save(out error), error ?? string.Empty);
            text = File.ReadAllText(path);
            Check("unknown groups stay in the file",
                text.Contains("[future]") && text.Contains("key=1") &&
                text.Contains("theme=light"), text);

            futureStore = SettingsFor(root);
            var futureGlossary = new TriggerGlossary(futureStore);
            futureGlossary.SetName(TriggerIcon.Gate, "门");
            futureGlossary.Save(out error);
            futureGlossary.Reset();
            Check("clearing the names keeps the theme",
                futureGlossary.Save(out error) &&
                new AppSettings(SettingsFor(root)).Theme == AppThemeMode.Light,
                error ?? string.Empty);
            Check("cleared names leave nothing behind",
                !File.ReadAllText(path).Contains("Gate="),
                File.ReadAllText(path));

            // 旧版本的两个文件：搬进新文件后删掉，键写坏的条目直接丢掉。
            string legacyRoot = Path.Combine(root, "legacy");
            Directory.CreateDirectory(legacyRoot);
            File.WriteAllText(Path.Combine(legacyRoot, "AppSettings.txt"),
                "# old\r\ntheme=light\r\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(legacyRoot, "TriggerGlossary.txt"),
                "# old\r\nBook=书\r\n坏行=坏\r\n", new UTF8Encoding(false));
            string legacyPath = Path.Combine(legacyRoot, SettingsStore.FileName);
            SettingsStore migrated = SettingsStore.Open(legacyPath, legacyRoot);
            Check("legacy settings are imported",
                new AppSettings(migrated).Theme == AppThemeMode.Light &&
                new TriggerGlossary(migrated).GetName(TriggerIcon.Book) == "书",
                legacyPath);
            Check("legacy junk keys are dropped",
                !File.ReadAllText(legacyPath).Contains("坏行"),
                File.ReadAllText(legacyPath));
            Check("legacy files are removed after the import",
                !File.Exists(Path.Combine(legacyRoot, "AppSettings.txt")) &&
                !File.Exists(Path.Combine(legacyRoot, "TriggerGlossary.txt")));
            Check("the import leaves the new file behind",
                File.Exists(legacyPath), legacyPath);
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
    /// 术语 page table: the card files keep the English trigger names, so the
    /// glossary only records the names the user renamed. The file is plain
    /// UTF-8 <c>English=中文</c>; clearing a name or deleting the file brings the
    /// built-in 魂 / 2魂 names back, and the editor accepts all three inputs
    /// (number, English name, Chinese name).
    /// </summary>
    private static void TestTriggerGlossary()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-glossary-" + Guid.NewGuid().ToString("N"));
        try
        {
            var glossary = new TriggerGlossary(SettingsFor(root));
            Check("glossary lists 23 editable triggers",
                TriggerGlossary.EditableIcons.Length == 23,
                TriggerGlossary.EditableIcons.Length.ToString());
            Check("glossary skips 无 and 魂",
                Array.IndexOf(TriggerGlossary.EditableIcons, TriggerIcon.None) < 0 &&
                Array.IndexOf(TriggerGlossary.EditableIcons, TriggerIcon.Soul) < 0);
            Check("glossary lives in the shared settings file",
                string.Equals(glossary.FilePath,
                    Path.Combine(root, SettingsStore.FileName),
                    StringComparison.OrdinalIgnoreCase), glossary.FilePath);
            Check("glossary has no file before the first save",
                !glossary.HasFile, glossary.FilePath);
            Check("glossary keeps the built-in 魂 / 2魂 names",
                glossary.GetName(TriggerIcon.Soul) == "魂" &&
                glossary.GetName(TriggerIcon.DoubleSoul) == "2魂",
                glossary.GetName(TriggerIcon.Soul) + "/" +
                glossary.GetName(TriggerIcon.DoubleSoul));
            Check("glossary falls back to the English name",
                glossary.GetName(TriggerIcon.Book) == "Book" &&
                !glossary.IsCustomized(TriggerIcon.Book),
                glossary.GetName(TriggerIcon.Book));

            string error;
            glossary.SetName(TriggerIcon.Book, " 书 ");
            Check("glossary trims the typed name",
                glossary.GetName(TriggerIcon.Book) == "书" &&
                glossary.IsCustomized(TriggerIcon.Book),
                glossary.GetName(TriggerIcon.Book));
            Check("glossary saves the table",
                glossary.Save(out error), error ?? string.Empty);

            string text = File.ReadAllText(glossary.FilePath);
            Check("glossary file stores the renamed trigger only",
                text.Contains("Book=书") && !text.Contains("Soul=") &&
                !text.Contains("Gate="), text);
            Check("glossary file starts with a comment",
                text.TrimStart().StartsWith("#", StringComparison.Ordinal),
                text);

            var reloaded = new TriggerGlossary(SettingsFor(root));
            Check("glossary reads the saved name back",
                reloaded.HasFile && reloaded.GetName(TriggerIcon.Book) == "书",
                reloaded.GetName(TriggerIcon.Book));

            File.WriteAllText(reloaded.FilePath,
                "# comment\r\n\r\n[terminology]\r\nGate=门\r\n不存在的图标=坏行\r\nBook = 书\r\n",
                new UTF8Encoding(false));
            reloaded.Load();
            Check("glossary ignores comments and junk lines",
                reloaded.GetName(TriggerIcon.Gate) == "门" &&
                reloaded.GetName(TriggerIcon.Book) == "书",
                reloaded.GetName(TriggerIcon.Gate) + "/" +
                reloaded.GetName(TriggerIcon.Book));
            Check("glossary ignores an unknown icon",
                !reloaded.IsCustomized(TriggerIcon.CustomIcon6));

            TriggerIcon parsed;
            Check("glossary parses the English name",
                TriggerGlossary.TryParse("gate", null, out parsed) &&
                parsed == TriggerIcon.Gate, parsed.ToString());
            Check("glossary parses a number",
                TriggerGlossary.TryParse("7", null, out parsed) &&
                parsed == (TriggerIcon)7, parsed.ToString());
            Check("glossary parses the built-in 2魂",
                TriggerGlossary.TryParse("2魂", null, out parsed) &&
                parsed == TriggerIcon.DoubleSoul, parsed.ToString());
            Check("glossary parses a custom Chinese name",
                TriggerGlossary.TryParse("门", reloaded, out parsed) &&
                parsed == TriggerIcon.Gate, parsed.ToString());
            Check("glossary refuses blank and unknown text",
                !TriggerGlossary.TryParse("  ", reloaded, out parsed) &&
                !TriggerGlossary.TryParse("没有这个", reloaded, out parsed) &&
                !TriggerGlossary.TryParse(null, reloaded, out parsed));
            Check("glossary refuses a number outside the enum",
                !TriggerGlossary.TryParse("99", null, out parsed) &&
                TriggerGlossary.TryParse("24", null, out parsed) &&
                parsed == TriggerIcon.CustomIcon6);

            reloaded.Reset();
            Check("glossary reset drops the custom names",
                reloaded.GetName(TriggerIcon.Gate) == "Gate" &&
                reloaded.GetName(TriggerIcon.Book) == "Book" &&
                !reloaded.IsCustomized(TriggerIcon.Book),
                reloaded.GetName(TriggerIcon.Gate));

            Check("glossary saves an emptied table",
                reloaded.Save(out error), error ?? string.Empty);
            string reset = File.ReadAllText(reloaded.FilePath);
            Check("glossary keeps only the header after a reset",
                !reset.Contains("Gate=") && !reset.Contains("Book="), reset);

            glossary.SetName(TriggerIcon.Book, string.Empty);
            Check("glossary drops a name cleared to blank",
                !glossary.IsCustomized(TriggerIcon.Book) &&
                glossary.GetName(TriggerIcon.Book) == "Book");
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
    /// The theme switch is only worth anything if the choice survives a
    /// restart: AppSettings reads and writes the [appearance] group of settings.txt,
    /// starts on dark, and never throws on a missing or broken file.
    /// </summary>
    private static void TestAppSettings()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new AppSettings(SettingsFor(root));
            Check("settings default to the dark theme",
                settings.Theme == AppThemeMode.Dark && !settings.HasFile,
                AppSettings.ToText(settings.Theme));
            Check("settings live in the shared settings file",
                string.Equals(settings.FilePath,
                    Path.Combine(root, SettingsStore.FileName),
                    StringComparison.OrdinalIgnoreCase), settings.FilePath);

            string error;
            settings.Theme = AppThemeMode.Light;
            Check("settings save the light theme",
                settings.Save(out error), error ?? string.Empty);

            string text = File.ReadAllText(settings.FilePath);
            Check("settings write theme=light", text.Contains("theme=light"),
                text);
            Check("settings start the file with a comment",
                text.TrimStart().StartsWith("#", StringComparison.Ordinal), text);

            var reloaded = new AppSettings(SettingsFor(root));
            Check("settings read the light theme back",
                reloaded.HasFile && reloaded.Theme == AppThemeMode.Light,
                AppSettings.ToText(reloaded.Theme));

            File.WriteAllText(reloaded.FilePath,
                "# note\r\n\r\n[appearance]\r\nTHEME = light\r\nunknown=1\r\n坏行\r\n",
                new UTF8Encoding(false));
            reloaded.Load();
            Check("settings ignore comments, case and junk lines",
                reloaded.Theme == AppThemeMode.Light,
                AppSettings.ToText(reloaded.Theme));

            File.WriteAllText(reloaded.FilePath, "theme=purple\r\n",
                new UTF8Encoding(false));
            reloaded.Load();
            Check("settings fall back to dark on an unknown value",
                reloaded.Theme == AppThemeMode.Dark,
                AppSettings.ToText(reloaded.Theme));

            reloaded.Theme = AppThemeMode.Light;
            File.Delete(reloaded.FilePath);
            reloaded.Load();
            Check("settings return to dark after the file is deleted",
                reloaded.Theme == AppThemeMode.Dark && !reloaded.HasFile,
                AppSettings.ToText(reloaded.Theme));

            AppThemeMode parsed;
            Check("settings parse the theme words",
                AppSettings.TryParseTheme(" Light ", out parsed) &&
                parsed == AppThemeMode.Light &&
                AppSettings.TryParseTheme("dark", out parsed) &&
                parsed == AppThemeMode.Dark);
            Check("settings refuse blank and unknown theme words",
                !AppSettings.TryParseTheme(string.Empty, out parsed) &&
                !AppSettings.TryParseTheme(null, out parsed) &&
                !AppSettings.TryParseTheme("purple", out parsed));

            Check("settings without a data root cannot save",
                !new AppSettings(null).Save(out error) &&
                !string.IsNullOrEmpty(error), error ?? string.Empty);
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
    /// The palette is the only place a theme knows its colours. Switching has
    /// to move all ten slots at once, and the repaint path relies on
    /// MapFromPrevious turning an old slot into the matching new one while
    /// leaving the accent, the status colours and white untouched.
    /// </summary>
    private static void TestAppPalette()
    {
        AppPalette.Select(AppThemeMode.Dark);
        Check("palette starts on the dark theme",
            ReferenceEquals(AppPalette.Current, AppPalette.Dark) &&
            ReferenceEquals(AppPalette.Previous, AppPalette.Dark));
        Check("palette resolves both themes and an unknown mode",
            ReferenceEquals(AppPalette.For(AppThemeMode.Dark), AppPalette.Dark) &&
            ReferenceEquals(AppPalette.For(AppThemeMode.Light), AppPalette.Light) &&
            ReferenceEquals(AppPalette.For((AppThemeMode)99), AppPalette.Dark));
        Check("palette keeps its colours when dark is selected again",
            AppPalette.MapFromPrevious(AppPalette.Dark.TileBackground) ==
                AppPalette.Dark.TileBackground);

        AppPalette.Select(AppThemeMode.Light);
        Check("palette switches to light and remembers dark",
            ReferenceEquals(AppPalette.Current, AppPalette.Light) &&
            ReferenceEquals(AppPalette.Previous, AppPalette.Dark));
        Check("palette maps every dark slot onto the light one",
            AppPalette.MapFromPrevious(AppPalette.Dark.Navigation) ==
                AppPalette.Light.Navigation &&
            AppPalette.MapFromPrevious(AppPalette.Dark.MainBackground) ==
                AppPalette.Light.MainBackground &&
            AppPalette.MapFromPrevious(AppPalette.Dark.TileBackground) ==
                AppPalette.Light.TileBackground &&
            AppPalette.MapFromPrevious(AppPalette.Dark.InputBackground) ==
                AppPalette.Light.InputBackground &&
            AppPalette.MapFromPrevious(AppPalette.Dark.Divider) ==
                AppPalette.Light.Divider &&
            AppPalette.MapFromPrevious(AppPalette.Dark.Border) ==
                AppPalette.Light.Border &&
            AppPalette.MapFromPrevious(AppPalette.Dark.Text) ==
                AppPalette.Light.Text &&
            AppPalette.MapFromPrevious(AppPalette.Dark.TextSoft) ==
                AppPalette.Light.TextSoft &&
            AppPalette.MapFromPrevious(AppPalette.Dark.TextMuted) ==
                AppPalette.Light.TextMuted &&
            AppPalette.MapFromPrevious(AppPalette.Dark.Scrim) ==
                AppPalette.Light.Scrim);
        Check("palette leaves accent, status and white alone",
            AppPalette.MapFromPrevious(AppTheme.Accent) == AppTheme.Accent &&
            AppPalette.MapFromPrevious(AppTheme.Danger) == AppTheme.Danger &&
            AppPalette.MapFromPrevious(Color.White) == Color.White &&
            AppPalette.MapFromPrevious(Color.Empty).IsEmpty);

        AppPalette.Select(AppThemeMode.Light);
        Check("palette ignores a repeated light select",
            ReferenceEquals(AppPalette.Previous, AppPalette.Dark));

        AppPalette.Select(AppThemeMode.Dark);
        Check("palette maps the light slots back",
            ReferenceEquals(AppPalette.Current, AppPalette.Dark) &&
            AppPalette.MapFromPrevious(AppPalette.Light.TileBackground) ==
                AppPalette.Dark.TileBackground);
        Check("theme reports the palette in use",
            AppTheme.Mode == AppThemeMode.Dark && AppTheme.IsDark &&
            AppTheme.TileBackground == AppPalette.Dark.TileBackground &&
            AppTheme.Text == AppPalette.Dark.Text);
    }

    /// <summary>
    /// 换主题不重建界面：<see cref="AppTheme.Recolor"/> 走一遍已经建好的控件树，
    /// 按槽位把写死的颜色换成新配色，圆角按钮 / 圆角输入框自己缓存的颜色也要
    /// 一起换，实现了 <see cref="IThemeAware"/> 的控件再收到一次 ApplyTheme。
    /// 控件只在内存里搭、不显示窗口，所以有没有交互桌面都能跑。
    /// </summary>
    private static void TestThemeRecolor()
    {
        AppTheme.Apply(AppThemeMode.Dark);

        var page = new Panel();
        page.BackColor = AppTheme.MainBackground;
        page.ForeColor = AppTheme.Text;

        var tile = new Panel();
        tile.BackColor = AppTheme.TileBackground;
        tile.ForeColor = AppTheme.TextMuted;
        page.Controls.Add(tile);

        var button = new RoundedButton();
        button.BackColor = AppTheme.Navigation;
        button.ForeColor = Color.White;
        button.NormalColor = AppTheme.Navigation;
        button.HoverColor = AppTheme.AccentHover;
        button.SelectedColor = AppTheme.Divider;
        button.SurfaceColor = AppTheme.InputBackground;
        page.Controls.Add(button);

        var input = new RoundedTextBox();
        input.FillColor = AppTheme.InputBackground;
        input.BorderColor = AppTheme.Border;
        page.Controls.Add(input);

        var probe = new ThemeAwareProbe();
        probe.BackColor = AppTheme.TileBackground;
        page.Controls.Add(probe);

        AppTheme.Apply(AppThemeMode.Light);
        AppTheme.Recolor(page);

        Check("recolor repaints the page itself",
            page.BackColor == AppPalette.Light.MainBackground &&
            page.ForeColor == AppPalette.Light.Text,
            page.BackColor.ToString());
        Check("recolor repaints nested controls",
            tile.BackColor == AppPalette.Light.TileBackground &&
            tile.ForeColor == AppPalette.Light.TextMuted,
            tile.BackColor.ToString());
        Check("recolor repaints a rounded button",
            button.BackColor == AppPalette.Light.Navigation &&
            button.NormalColor == AppPalette.Light.Navigation &&
            button.SelectedColor == AppPalette.Light.Divider &&
            button.SurfaceColor == AppPalette.Light.InputBackground,
            button.NormalColor.ToString());
        Check("recolor leaves white text and the accent alone",
            button.ForeColor == Color.White &&
            button.HoverColor == AppTheme.AccentHover,
            button.HoverColor.ToString());
        Check("recolor repaints a rounded input",
            input.FillColor == AppPalette.Light.InputBackground &&
            input.BorderColor == AppPalette.Light.Border,
            input.FillColor.ToString());
        Check("recolor asks a theme aware control to rebuild",
            probe.ApplyThemeCount == 1 &&
            probe.BackColor == AppPalette.Light.TileBackground,
            "count=" + probe.ApplyThemeCount);
        Check("foreground flips with the brightness of the fill",
            AppTheme.ForegroundOn(AppTheme.Accent) == Color.White &&
            AppTheme.ForegroundOn(Color.White) == AppPalette.Light.Text &&
            AppTheme.ForegroundOn(Color.Empty) == AppPalette.Light.Text,
            AppTheme.ForegroundOn(AppTheme.Accent).ToString());

        AppTheme.Apply(AppThemeMode.Dark);
        page.Dispose();

        Check("recolor goes back to the dark colours",
            AppTheme.MainBackground == AppPalette.Dark.MainBackground &&
            AppTheme.Text == AppPalette.Dark.Text);
    }

    /// <summary>
    /// 只等着 <see cref="AppTheme.Recolor"/> 叫它一声的控件，用来确认重绘真的
    /// 通知到了缓存画刷的那一层。
    /// </summary>
    private sealed class ThemeAwareProbe : Panel, IThemeAware
    {
        public int ApplyThemeCount { get; private set; }

        public void ApplyTheme()
        {
            ApplyThemeCount++;
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

            // The 缩略 sheet stamps the repeat count the way the deck grid
            // does: a round sticker in the bottom right corner of the card.
            // The old export drew a rounded "xN" pill on the top right.
            float stickerScale = 252F / 176F;
            var cardTile = new Rectangle(56, 142, 252, 352);
            Rectangle sticker = DeckExportService.PlaceCountBadge(cardTile, 40,
                stickerScale);
            Check("deck count sticker sits in the card's bottom right corner",
                sticker.Width == 40 && sticker.Height == 40 &&
                sticker.Right == cardTile.Right - 6 &&
                sticker.Bottom == cardTile.Bottom - 6,
                sticker.ToString());
            Check("deck count sticker scales its gap with the card",
                DeckExportService.CountBadgeMargin(1F) == 4 &&
                DeckExportService.CountBadgeMargin(stickerScale) == 6 &&
                DeckExportService.CountBadgePadding(1F) == 3 &&
                DeckExportService.CountBadgePadding(stickerScale) == 4,
                DeckExportService.CountBadgeMargin(stickerScale) + "/" +
                    DeckExportService.CountBadgePadding(stickerScale));
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

    /// <summary>
    /// The lines of one card block of a CardData text: the `Character:` /
    /// `Event:` / `Climax:` header down to its `EndCard`, inclusive. Returns an
    /// empty array when the id has no block.
    /// </summary>
    private static string[] ReadCardBlock(string text, string id)
    {
        string[] lines = CardDataWriter.SplitLines(text);
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            int colon = trimmed.IndexOf(':');
            if (colon <= 0)
                continue;

            string head = trimmed.Substring(0, colon);
            if (!string.Equals(head, "Character", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(head, "Event", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(head, "Climax", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(trimmed.Substring(colon + 1).Trim(), id,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (int j = i; j < lines.Length; j++)
            {
                if (string.Equals(lines[j].Trim(), "EndCard",
                    StringComparison.OrdinalIgnoreCase))
                {
                    var block = new string[j - i + 1];
                    Array.Copy(lines, i, block, 0, block.Length);
                    return block;
                }
            }
            return new string[0];
        }
        return new string[0];
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

    /// <summary>
    /// 图片来源：一个卡包目录只枚举一次（以前按 png/jpg/jpeg/webp 各扫一遍，
    /// 打开系列时白花 ~20 ms）。索引要同时收完整文件名和去掉扩展名的名字，
    /// 图片以外的文件不能混进来，卡号与文件名对不上时仍要靠候选名找到图。
    /// </summary>
    private static void TestImageIndex()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-imageindex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "CardData.txt"), "Character:A");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "n");
            File.WriteAllBytes(Path.Combine(root, "HOL_W104_041.png"),
                new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(root, "AW_S18_E002.jpg"),
                new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(root, "AW_S18_003.jpeg"),
                new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(root, "AW_S18_004.webp"),
                new byte[] { 1 });

            Dictionary<string, string> index = ImageLocator.BuildIndex(root);
            Check("image index covers four images", index.Count == 8,
                index.Count.ToString());
            Check("image index skips non-image files",
                !index.ContainsKey("CardData.txt") &&
                !index.ContainsKey("notes.txt"));
            Check("image index keeps a jpeg name",
                index.ContainsKey("AW_S18_003.jpeg"));
            Check("image index keeps a base name",
                index.ContainsKey("AW_S18_004"));
            Check("image index ignores case",
                index.ContainsKey("hol_w104_041.PNG"));

            // The card code rarely matches the file name: the / and - spellings
            // have to be tried, and the parallel E is often only in the file.
            var card = new WeissCard();
            card.Id = "AW/S18-002";
            card.ImageId = "AW/S18-002";
            ImageLocator.Resolve(card, index);
            Check("image index resolves the parallel E",
                card.ImagePath == Path.Combine(root, "AW_S18_E002.jpg"),
                card.ImagePath ?? "missing");

            var missing = new WeissCard();
            missing.Id = "AW/S18-999";
            missing.ImageId = "AW/S18-999";
            ImageLocator.Resolve(missing, index);
            Check("image index leaves an unknown card empty",
                string.IsNullOrEmpty(missing.ImagePath),
                missing.ImagePath ?? "null");
        }
        finally
        {
            try
            {
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

    /// <summary>
    /// 编辑卡片 writes CardData.txt in place. These checks cover the block
    /// text, a parse round trip, the in-place update and append, the encoding
    /// and line ending of the surrounding file, and the card art copy.
    /// </summary>
    private static void TestCardEditor()
    {
        TestCardEditorBlock();
        TestCardEditorSave();
        TestCardEditorPatch();
        TestCardEditorEncoding();
        TestCardEditorImage();
        TestCardReferenceIndex();
    }

    /// <summary>
    /// Builds the real 编辑卡片 page on an STA thread, the same way the import
    /// overlay test does, so a layout or control mistake fails the suite
    /// instead of the first user who opens 编辑卡片.
    /// </summary>
    private static void TestCardEditorPage()
    {
        Exception error = null;
        var worker = new Thread(delegate()
        {
            try
            {
                TestCardEditorPageCore();
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
            Fail("cardedit editor page runs without a screen", error.ToString());
    }

    private static void TestCardEditorPageCore()
    {
        var series = new SeriesDefinition("测试系列");
        var folder = CreateTestFolder(Path.GetTempPath());
        series.Folders.Add(folder);

        CardEditorDraft draft = CardEditorDraft.CreateBlank(series, folder);
        draft.Id = "TST/09-E001";
        draft.Name = "编辑器测试";
        draft.Level = 2;
        draft.Power = 6000;
        draft.Trigger = TriggerIcon.Book;
        draft.Combo = "テストコンボ";
        draft.EffectCode = "Auto: OnAttack\n{\n        Draw 1\n}";

        using (var page = new CardEditorPage())
        {
            int closings = 0;
            page.CloseRequested += delegate { closings++; };
            page.SaveRequested += delegate { };
            page.LoadDraft(draft, series.Folders);

            Check("cardedit page shows the draft",
                page.Draft != null && page.Draft.Name == "编辑器测试",
                page.Draft == null ? "null" : page.Draft.Name);

            CardEditorDraft read = page.ReadDraft();
            Check("cardedit page reads the fields back",
                read != null && read.Id == "TST/09-E001" && read.Level == 2 &&
                read.Power == 6000 && read.Trigger == TriggerIcon.Book,
                read == null ? "null" : read.Id + "/" + read.Level);
            Check("cardedit page keeps the combo",
                read != null && read.Combo == "テストコンボ",
                read == null ? "null" : read.Combo);
            Check("cardedit page keeps the effect code",
                read != null && read.EffectCode.Contains("Draw 1"),
                read == null ? "null" : read.EffectCode);

            // 术语: the 触发 field prints the glossary name, accepts it back and
            // still understands the English name the card table stores.
            string glossaryRoot = Path.Combine(Path.GetTempPath(),
                "ws-cardedit-glossary-" + Guid.NewGuid().ToString("N"));
            try
            {
                var glossary = new TriggerGlossary(SettingsFor(glossaryRoot));
                glossary.SetName(TriggerIcon.Book, "书");
                page.Glossary = glossary;
                page.LoadDraft(draft, series.Folders);

                FieldBox triggerBox = FindEditorField(page, "_triggerBox");
                Check("cardedit page shows the Chinese 触发 name",
                    triggerBox != null && triggerBox.Input.Text == "书",
                    triggerBox == null ? "null" : triggerBox.Input.Text);

                if (triggerBox != null)
                {
                    triggerBox.Input.Text = "魂";
                    CardEditorDraft soulDraft = page.ReadDraft();
                    Check("cardedit page accepts the built-in 魂 name",
                        soulDraft != null && soulDraft.Trigger == TriggerIcon.Soul,
                        soulDraft == null ? "null" : soulDraft.Trigger.ToString());

                    triggerBox.Input.Text = "书";
                    CardEditorDraft bookDraft = page.ReadDraft();
                    Check("cardedit page accepts a custom 触发 name",
                        bookDraft != null && bookDraft.Trigger == TriggerIcon.Book,
                        bookDraft == null ? "null" : bookDraft.Trigger.ToString());

                    triggerBox.Input.Text = "Gate";
                    CardEditorDraft gateDraft = page.ReadDraft();
                    Check("cardedit page still accepts the English 触发 name",
                        gateDraft != null && gateDraft.Trigger == TriggerIcon.Gate,
                        gateDraft == null ? "null" : gateDraft.Trigger.ToString());

                    triggerBox.Input.Text = "没有这个触发";
                    Check("cardedit page rejects an unknown 触发 name",
                        page.ReadDraft() == null);
                }

                page.Glossary = null;
                draft.Trigger = TriggerIcon.Book;
                page.LoadDraft(draft, series.Folders);
                FieldBox englishBox = FindEditorField(page, "_triggerBox");
                Check("cardedit page falls back to the English 触发 name",
                    englishBox != null && englishBox.Input.Text == "Book",
                    englishBox == null ? "null" : englishBox.Input.Text);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(glossaryRoot))
                        Directory.Delete(glossaryRoot, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            FieldInfo codeField = typeof(CardEditorPage).GetField("_codeBox",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var codeBox = codeField == null ? null : codeField.GetValue(page) as FieldBox;
            Check("cardedit page code box is multiline",
                codeBox != null && codeBox.Input.Multiline);
            Check("cardedit page code box keeps the line breaks",
                codeBox != null && codeBox.Input.Text.Contains("\r\n"),
                codeBox == null ? "null" : codeBox.Input.Text);

            FieldBox comboBox = FindEditorField(page, "_comboBox");
            Check("cardedit page has a combo field",
                comboBox != null && comboBox.Input.Text == "テストコンボ",
                comboBox == null ? "null" : comboBox.Input.Text);
            Check("cardedit page labels the combo field 联动",
                HasLabelText(page, "联动 (Combo)"));
            Check("cardedit page labels the backup field 反击",
                HasLabelText(page, "反击"));
            Check("cardedit page has two reference searches",
                FindEditorField(page, "_refNameBox") != null &&
                FindEditorField(page, "_refTextBox") != null);

            // A blank 卡号 must be refused instead of writing a broken block.
            SetEditorText(page, "_idBox", string.Empty);
            Check("cardedit page refuses an empty card number",
                page.ReadDraft() == null);
            SetEditorText(page, "_idBox", "TST/09-E001");

            // 反击 must be an integer, and the complaint has to use the label
            // the editor shows (the CardData line is still `Backup`).
            SetEditorText(page, "_backupBox", "abc");
            Check("cardedit page refuses a broken 反击 value",
                page.ReadDraft() == null && StatusText(page).Contains("反击"),
                StatusText(page));
            SetEditorText(page, "_backupBox", "0");

            // Lay the page out at the default window size and paint it once,
            // so a broken table row or a null control fails here instead of on
            // the first machine that opens 编辑卡片.
            page.Size = new Size(1400, 900);
            page.CreateControl();
            page.PerformLayout();
            using (var bitmap = new Bitmap(1400, 900))
            {
                page.DrawToBitmap(bitmap, new Rectangle(0, 0, 1400, 900));
                Check("cardedit page paints without a screen",
                    bitmap.Width == 1400 && bitmap.Height == 900);

                Color background = page.BackColor;
                bool painted = false;
                for (int y = 60; y < 860 && !painted; y += 10)
                {
                    for (int x = 130; x < 1270 && !painted; x += 10)
                        painted = bitmap.GetPixel(x, y).ToArgb() !=
                            Color.Black.ToArgb();
                }
                Check("cardedit page paints its surface", painted,
                    background.ToString());
            }

            // 中栏 效果内容 : 效果代码 = 1 : 2，右栏参考详情里的两块只读框必须
            // 填满各自那一行（漏设 Dock 时它们只有 200×100 的默认尺寸）。
            LayOutDeep(page);
            FieldBox effectBox = FindEditorField(page, "_textBox");
            Check("cardedit page splits the effect column 1:2",
                effectBox != null && codeBox != null && effectBox.Height > 0 &&
                codeBox.Height >= effectBox.Height * 1.8F &&
                codeBox.Height <= effectBox.Height * 2.2F,
                (effectBox == null ? "null" : effectBox.Height.ToString()) + "/" +
                (codeBox == null ? "null" : codeBox.Height.ToString()));

            // 多效果: 每条效果一行，内容紧挨着它自己的代码，而不是全部内容
            // 挤在上面、全部代码挤在下面。
            CardEditorDraft two = CardEditorDraft.CreateBlank(series, folder);
            two.IsNew = false;
            two.Id = "TST/09-E002";
            two.Name = "两条效果";
            two.EffectCode =
                "Auto: OnPlay\n{\n        Draw 1\n}\n" +
                "Cont: GiveSelf\n{\n        GainPower 1000\n}";
            two.TextLines = new List<string> { "第一条", "第二条" };
            two.Text = "第一条\n第二条";
            page.LoadDraft(two, null);

            System.Collections.IList rows = EditorEffectRows(page);
            Check("cardedit page shows one row per 效果",
                rows != null && rows.Count == 2,
                rows == null ? "null" : rows.Count.ToString());
            Check("cardedit page numbers the second 效果",
                HasLabelText(page, "效果 2 · 内容（卡面上印的效果文字）") &&
                HasLabelText(page, "效果 2 · 代码（Act / Auto / Cont 与 { } 代码块）"));

            CardEditorDraft readTwo = page.ReadDraft();
            Check("cardedit page reads both 效果 back",
                readTwo != null && readTwo.Text == "第一条\n第二条" &&
                readTwo.EffectCode.Contains("GainPower 1000") &&
                readTwo.TextLines != null && readTwo.TextLines.Count == 2,
                readTwo == null ? "null" : readTwo.Text);
            IList<CardEffectPair> readPairs =
                CardEffectSplitter.PairDraft(readTwo);
            Check("cardedit page keeps both 效果 paired",
                readPairs.Count == 2 && readPairs[1].Text == "第二条" &&
                readPairs[1].Code.Contains("GainPower 1000") &&
                !readPairs[1].Code.Contains("Draw 1"),
                readPairs.Count + " / " +
                (readPairs.Count > 1 ? readPairs[1].Code : "?"));

            // 简写行: 效果内容写成 `*Shorthand` 时，这一行的“简写提供（只读）”
            // 跟 内容 / 代码 一样是一行普通小标题（多条效果时带“效果 N ·”编号），
            // 下面的只读框占满整列、只有一条文字那么高（1-2 行）；可编辑的
            // “效果内容”只装卡块自己的
            // `Text` 行，自己没有文字时连输入框一起省掉；读回草稿后，每一行自己
            // 的文字仍写回它自己那段代码（简写那两段没有自己的 `Text`，后面的
            // 效果不能因此往前挪一行）。
            CardEditorDraft shorthand = CardEditorDraft.CreateBlank(series, folder);
            shorthand.IsNew = false;
            shorthand.Id = "TST/09-E003";
            shorthand.Name = "简写行";
            shorthand.EffectCode =
                "*EncoreAll\n*HealOne\n" +
                "Cont: GiveSelf\n{\n        GainPower 1000\n}";
            shorthand.TextLines = new List<string>
            {
                "【自】简写正文一", "【起】简写正文二", "自己的文字"
            };
            shorthand.Text = "【自】简写正文一\n【起】简写正文二\n自己的文字";
            shorthand.TextBlockIndexes = new List<int> { 0, 1, 2 };
            shorthand.TextInherited = new List<bool> { true, true, false };
            page.LoadDraft(shorthand, null);

            System.Collections.IList shorthandRows = EditorEffectRows(page);
            Check("cardedit page shows one row per 效果 of a shorthand card",
                shorthandRows != null && shorthandRows.Count == 3,
                shorthandRows == null ? "null" : shorthandRows.Count.ToString());
            if (shorthandRows != null && shorthandRows.Count == 3)
            {
                FieldBox firstPreview = FindEffectRowBox(shorthandRows[0], "Inherited");
                FieldBox firstText = FindEffectRowBox(shorthandRows[0], "Text");
                FieldBox thirdPreview = FindEffectRowBox(shorthandRows[2], "Inherited");
                FieldBox thirdText = FindEffectRowBox(shorthandRows[2], "Text");
                Check("cardedit page shows the shorthand text read-only",
                    firstPreview != null && firstPreview.Input.ReadOnly &&
                    firstPreview.Input.Text == "【自】简写正文一",
                    firstPreview == null ? "null" : firstPreview.Input.Text);
                Check("cardedit page keeps the own text out of the preview",
                    firstText != null && firstText.Input.Text.Length == 0,
                    firstText == null ? "null" : firstText.Input.Text);
                Check("cardedit page keeps the own text of the third 效果",
                    thirdText != null && thirdText.Input.Text == "自己的文字",
                    thirdText == null ? "null" : thirdText.Input.Text);
                Check("cardedit page hides the preview without a shorthand",
                    thirdPreview != null &&
                    EffectRowPreviewHeight(shorthandRows[0]) > 0 &&
                    EffectRowPreviewHeight(shorthandRows[2]) == 0,
                    thirdPreview == null ? "null" :
                        EffectRowPreviewHeight(shorthandRows[0]) + "/" +
                        EffectRowPreviewHeight(shorthandRows[2]));
                // 标题跟 内容 / 代码 一样是普通的一行小标题，只读框在它下面占满
                // 整列：不再缩成左边一列标签 + 右边一条窄框。
                LayOutDeep(page);
                Label shorthandCaption = EffectRowLabel(shorthandRows[0],
                    "InheritedCaption");
                Control shorthandBand = EffectRowControl(shorthandRows[0],
                    "InheritedCell");
                int captionNeeded = shorthandCaption == null ? -1 :
                    TextRenderer.MeasureText(shorthandCaption.Text,
                        shorthandCaption.Font).Width;
                Check("cardedit page titles the 简写 band like the other boxes",
                    shorthandCaption != null &&
                    shorthandCaption.Text == "效果 1 · 简写提供（只读）",
                    shorthandCaption == null ? "null" : shorthandCaption.Text);
                Check("cardedit page shows the whole 简写 caption",
                    shorthandCaption != null && captionNeeded > 0 &&
                    shorthandCaption.Width >= captionNeeded,
                    captionNeeded + " → " + (shorthandCaption == null ? "null" :
                        shorthandCaption.Width.ToString()));
                Check("cardedit page puts the 简写 caption above its box",
                    shorthandCaption != null && firstPreview != null &&
                    shorthandBand != null &&
                    shorthandCaption.Bottom <= firstPreview.Top &&
                    shorthandCaption.Left <= firstPreview.Left + 3,
                    shorthandCaption == null || firstPreview == null ? "null" :
                        shorthandCaption.Bottom + " / " + firstPreview.Top);
                Check("cardedit page fills the 简写 band with its box",
                    shorthandBand != null && firstPreview != null &&
                    firstPreview.Width >= shorthandBand.Width - 8 &&
                    firstPreview.Left <= shorthandBand.Left + 1,
                    (shorthandBand == null ? "null" : shorthandBand.Width.ToString()) +
                        " / " + (firstPreview == null ? "null" :
                        firstPreview.Width.ToString()));
                // 框的高度就是行里给带子量出来的高度减去标题行 17 和间隙 2：
                // 文字既不会被裁掉，也不会在下面留一条空。
                Check("cardedit page fits the 简写 box to its height",
                    firstPreview != null &&
                    Math.Abs(firstPreview.Height -
                        (EffectRowPreviewHeight(shorthandRows[0]) - 19)) <= 1 &&
                    firstPreview.Input.Height >= firstPreview.Input.Font.Height,
                    firstPreview == null ? "null" :
                        firstPreview.Height + " / " + (EffectRowPreviewHeight(
                            shorthandRows[0]) - 19) + " / " +
                        firstPreview.Input.Height);
                // 带子高度 = 标题行 17 + 文字行 + 框内上下留白 16 + 间隙 2，
                // 所以一行简写约 51，两行约 69，都远小于一整块固定高度。
                Check("cardedit page keeps the 简写 band to one text line",
                    EffectRowPreviewHeight(shorthandRows[0]) >= 32 &&
                    EffectRowPreviewHeight(shorthandRows[0]) <= 96,
                    EffectRowPreviewHeight(shorthandRows[0]).ToString());
                Check("cardedit page omits the empty 内容 box of a 简写 row",
                    EffectRowTextHidden(shorthandRows[0]) &&
                    EffectRowTextHidden(shorthandRows[1]) &&
                    !EffectRowTextHidden(shorthandRows[2]),
                    EffectRowTextHidden(shorthandRows[0]) + "/" +
                    EffectRowTextHidden(shorthandRows[1]) + "/" +
                    EffectRowTextHidden(shorthandRows[2]));

                // 预览高度跟着文字行数走：一行就一行高，两行才两行高。
                int oneLine = EffectRowPreviewHeight(shorthandRows[0]);
                SetEffectRowInheritedText(page, shorthandRows[0],
                    "【自】第一行\n【起】第二行");
                int twoLines = EffectRowPreviewHeight(shorthandRows[0]);
                SetEffectRowInheritedText(page, shorthandRows[0], "【自】简写正文一");
                Check("cardedit page grows the 简写 band line by line",
                    twoLines > oneLine &&
                    EffectRowPreviewHeight(shorthandRows[0]) == oneLine,
                    oneLine + " → " + twoLines);
            }

            CardEditorDraft readShorthand = page.ReadDraft();
            IList<CardEffectPair> writePairs = readShorthand == null
                ? new List<CardEffectPair>()
                : CardEffectSplitter.PairDraftForWrite(readShorthand);
            Check("cardedit page writes the shorthand card text back in place",
                writePairs.Count == 3 &&
                writePairs[0].Text.Length == 0 &&
                writePairs[1].Text.Length == 0 &&
                writePairs[2].Text == "自己的文字" &&
                writePairs[2].Code.Contains("GainPower 1000"),
                writePairs.Count + " / " +
                (writePairs.Count > 2 ? writePairs[2].Text + " @ " +
                    writePairs[2].Code.Replace("\n", " ") : "?"));

            page.LoadDraft(two, null);
            AddEditorEffectRow(page);
            Check("cardedit page adds an 效果 row",
                EditorEffectRows(page).Count == 3);
            page.LoadDraft(draft, series.Folders);
            Check("cardedit page drops the extra row on reload",
                EditorEffectRows(page).Count == 1);

            Control detailPanel = FindEditorControl(page, "_referenceDetailPanel");
            if (detailPanel != null)
                detailPanel.Visible = true;
            LayOutDeep(page);
            FieldBox refText = FindEditorField(page, "_refTextBox2");
            FieldBox refCode = FindEditorField(page, "_refCodeBox");
            Check("cardedit reference detail boxes fill their rows",
                refText != null && refCode != null &&
                refText.Dock == DockStyle.Fill && refCode.Dock == DockStyle.Fill &&
                refText.Height > 140 && refCode.Height > refText.Height,
                (refText == null ? "null" : refText.Width + "x" + refText.Height) +
                "/" + (refCode == null ? "null" : refCode.Width + "x" + refCode.Height));

            Check("cardedit page splits 1/4 + 3/8 + 3/8",
                HasEditorColumns(page), "body columns not found");

            // 添加卡片 opens without a series: the 卡号 picks the card pack, so
            // 保存 never asks for one. These checks drive the real text box.
            var packSeries = new SeriesDefinition("测试系列");
            SeriesFolder s09 = CreatePackFolder("测试系列/S09", "TST/09", "S09", "测试系列");
            s09.FullPath = Path.Combine(Path.GetTempPath(), "测试系列", "S09");
            packSeries.Folders.Add(s09);
            page.PackIndex = CardPackIndex.Build(
                new List<SeriesDefinition> { packSeries },
                delegate(SeriesFolder pack) { return pack.FullPath; });

            CardEditorDraft add = CardEditorDraft.CreateBlank(null, null);
            page.LoadDraft(add, null);
            Check("cardedit add opens without a series",
                !add.HasFolder && page.Draft == add);

            SetEditorText(page, "_idBox", "TST/09-E101");
            Check("cardedit add picks the pack from the card number",
                add.HasFolder && add.FolderPath == "测试系列/S09" &&
                add.SetName == "S09" && add.SeriesName == "测试系列",
                add.FolderPath + " / " + add.SetName);
            Check("cardedit add names the pack in the header",
                page.Subtitle.Contains("S09"), page.Subtitle);
            Check("cardedit add saves without a second step",
                page.ReadDraft() != null && page.Draft.HasFolder);

            // A prefix two packs share is offered as chips and still resolves
            // to the best candidate.
            SeriesFolder s09Pr = CreatePackFolder("测试系列/PR", "TST/09", "PR", "测试系列");
            s09Pr.FullPath = Path.Combine(Path.GetTempPath(), "测试系列", "PR");
            packSeries.Folders.Add(s09Pr);
            page.PackIndex = CardPackIndex.Build(
                new List<SeriesDefinition> { packSeries },
                delegate(SeriesFolder pack) { return pack.FullPath; });
            SetEditorText(page, "_idBox", "TST/09-E102");
            FieldInfo choicesField = typeof(CardEditorPage).GetField(
                "_packChoicesVisible", BindingFlags.Instance | BindingFlags.NonPublic);
            bool choices = choicesField != null &&
                (bool)choicesField.GetValue(page);
            Check("cardedit add offers a shared prefix as chips",
                choices && add.FolderPath == "测试系列/S09",
                add.FolderPath + " / chips=" + choices);

            // An unknown prefix has to stop 保存 with a readable reason instead
            // of writing into the pack picked before.
            SetEditorText(page, "_idBox", "ZZZ/99-001");
            CardEditorDraft refused = page.ReadDraft();
            Check("cardedit add refuses an unknown prefix",
                refused == null && StatusText(page).Contains("没有匹配的卡包"),
                StatusText(page));
            SetEditorText(page, "_idBox", "TST/09-E102");

            // A blank 卡号 in a multi-pack series must not offer the pack chips:
            // the number picks the pack, so the row only appears once the
            // number is ambiguous (or unknown). It used to stay filled when
            // 添加卡片 was opened right after 修改卡片.
            CardEditorDraft blankAdd = CardEditorDraft.CreateBlank(packSeries, null);
            page.LoadDraft(blankAdd, packSeries.Folders);
            bool blankChips = choicesField != null &&
                (bool)choicesField.GetValue(page);
            Check("cardedit add hides the pack row while the card number is empty",
                !blankChips && !blankAdd.HasFolder,
                "chips=" + blankChips + " / folder=" + blankAdd.FolderPath);
            Check("cardedit add asks for the card number instead",
                page.Subtitle.Contains("输入卡号后自动分配卡包"), page.Subtitle);

            page.UnloadDraft();
            Check("cardedit page drops the draft", page.Draft == null);
            Check("cardedit page did not close itself", closings == 0,
                "closes=" + closings);
        }
    }

    /// <summary>
    /// Finds the 1/4 + 3/8 + 3/8 body table the editor page builds.
    /// </summary>
    private static bool HasEditorColumns(Control root)
    {
        var panel = root as TableLayoutPanel;
        if (panel != null && panel.ColumnStyles.Count == 3)
        {
            if (panel.ColumnStyles[0].Width == 25F &&
                panel.ColumnStyles[1].Width == 37.5F &&
                panel.ColumnStyles[2].Width == 37.5F)
                return true;
        }

        for (int i = 0; i < root.Controls.Count; i++)
        {
            if (HasEditorColumns(root.Controls[i]))
                return true;
        }

        return false;
    }

    private static FieldBox FindEditorField(object owner, string field)
    {
        FieldInfo info = owner.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic);
        return info == null ? null : info.GetValue(owner) as FieldBox;
    }

    /// <summary>The rows of the 中栏 效果 list, one per 效果.</summary>
    private static System.Collections.IList EditorEffectRows(object owner)
    {
        FieldInfo info = owner.GetType().GetField("_effectRows",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return info == null ? null : info.GetValue(owner) as System.Collections.IList;
    }

    /// <summary>Adds one empty 效果 row (the ＋ 添加效果 button path).</summary>
    private static void AddEditorEffectRow(object owner)
    {
        MethodInfo method = owner.GetType().GetMethod("AddEffectRow",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method != null)
            method.Invoke(owner, new object[] { new CardEffectPair(), false });
    }

    /// <summary>One FieldBox of an 效果 row (`Text`, `Code` or `Inherited`).</summary>
    private static FieldBox FindEffectRowBox(object row, string field)
    {
        if (row == null)
            return null;
        FieldInfo info = row.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return info == null ? null : info.GetValue(row) as FieldBox;
    }

    /// <summary>One labelled control of an 效果 row (`TextCaption`,
    /// `CodeCaption` or `InheritedCaption`).</summary>
    private static Label EffectRowLabel(object row, string field)
    {
        if (row == null)
            return null;
        FieldInfo info = row.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return info == null ? null : info.GetValue(row) as Label;
    }

    /// <summary>One wrapper of an 效果 row (`Cell`, `TextCell` or
    /// `InheritedCell`), used to compare the room a band gives its parts.</summary>
    private static Control EffectRowControl(object row, string field)
    {
        if (row == null)
            return null;
        FieldInfo info = row.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return info == null ? null : info.GetValue(row) as Control;
    }

    /// <summary>Height the row gives its 简写提供 preview, 0 without one.</summary>
    private static int EffectRowPreviewHeight(object row)
    {
        if (row == null)
            return -1;
        FieldInfo info = row.GetType().GetField("InheritedHeight",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return info == null ? -1 : (int)info.GetValue(row);
    }

    /// <summary>True while the row hides its 效果内容 box because the
    /// `*Shorthand` already prints the whole 效果.</summary>
    private static bool EffectRowTextHidden(object row)
    {
        if (row == null)
            return false;
        FieldInfo info = row.GetType().GetField("TextHidden",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return info != null && (bool)info.GetValue(row);
    }

    /// <summary>Feeds one 简写提供 text straight into an 效果 row, the same way
    /// the page fills it when the card is loaded.</summary>
    private static void SetEffectRowInheritedText(object page, object row, string text)
    {
        if (page == null || row == null)
            return;
        MethodInfo method = page.GetType().GetMethod("ApplyInheritedText",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method != null)
            method.Invoke(page, new object[] { row, text });
    }

    private static void SetEditorText(object owner, string field, string text)
    {
        FieldBox box = FindEditorField(owner, field);
        if (box != null)
            box.Input.Text = text;
    }

    private static Control FindEditorControl(object owner, string field)
    {
        FieldInfo info = owner.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic);
        return info == null ? null : info.GetValue(owner) as Control;
    }

    /// <summary>
    /// Lays out the page and every nested table: a page that was never shown
    /// otherwise only lays out one level deep, so row heights stay defaults.
    /// </summary>
    private static void LayOutDeep(Control control)
    {
        control.PerformLayout();
        for (int i = 0; i < control.Controls.Count; i++)
            LayOutDeep(control.Controls[i]);
    }

    private static string StatusText(object owner)
    {
        FieldInfo info = owner.GetType().GetField("_statusLabel",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Label label = info == null ? null : info.GetValue(owner) as Label;
        return label == null ? string.Empty : label.Text;
    }

    private static bool HasLabelText(Control root, string text)
    {
        var label = root as Label;
        if (label != null && label.Text != null &&
            label.Text.IndexOf(text, StringComparison.Ordinal) >= 0)
            return true;

        for (int i = 0; i < root.Controls.Count; i++)
        {
            if (HasLabelText(root.Controls[i], text))
                return true;
        }

        return false;
    }

    private static CardEditorDraft CreateTestDraft(string id)
    {
        var draft = new CardEditorDraft();
        draft.Id = id;
        draft.Name = "测试卡";
        draft.JapaneseName = "テストカード";
        draft.Category = CardCategory.Character;
        draft.Color = CardColor.Red;
        draft.Level = 1;
        draft.Cost = 0;
        draft.Power = 5000;
        draft.Soul = 1;
        draft.Trigger = TriggerIcon.Soul;
        draft.Backup = 500;
        draft.Trait1 = "テスト";
        draft.Trait2 = "カード";
        draft.Combo = "テストコンボ";
        draft.Text = "【自】第一行\n第二行";
        draft.EffectCode = "Auto: StartClimaxPhase\n{\n        PayCost Clock\n}";
        return draft;
    }

    private static SeriesFolder CreateTestFolder(string fullPath)
    {
        var folder = new SeriesFolder();
        folder.SeriesName = "测试系列";
        folder.FolderPath = "测试系列/S01";
        folder.Prefix = "TST";
        folder.SetName = "S01";
        folder.FullPath = fullPath;
        folder.FileName = "CardData";
        return folder;
    }

    private static List<WeissCard> ParseTestFolder(SeriesFolder folder)
    {
        return ParseTestFolder(folder, CommonEffectLibrary.Empty);
    }

    private static List<WeissCard> ParseTestFolder(SeriesFolder folder,
        CommonEffectLibrary commonEffects)
    {
        var warnings = new List<string>();
        return CardDataParser.ParseFile(
            Path.Combine(folder.FullPath, "CardData.txt"),
            folder, commonEffects, warnings);
    }

    private static void TestCardEditorBlock()
    {
        CardEditorDraft draft = CreateTestDraft("TST/01-E001");
        string block = CardDataWriter.BuildBlock(draft);

        Check("cardedit block header",
            block.StartsWith("Character: TST/01-E001", StringComparison.Ordinal),
            block);
        Check("cardedit block name", block.Contains("Name 测试卡"), block);
        Check("cardedit block writes the JP name",
            block.Contains("JPName テストカード") && !block.Contains("JpName "),
            block);
        Check("cardedit block writes the combo",
            block.Contains("Combo テストコンボ"), block);
        Check("cardedit block color", block.Contains("Color R"), block);
        Check("cardedit block power", block.Contains("Power 5000"), block);
        Check("cardedit block trigger", block.Contains("Trigger Soul"), block);
        Check("cardedit block backup", block.Contains("Backup 500"), block);
        Check("cardedit block traits", block.Contains("Trait2 カード"), block);
        Check("cardedit block keeps the effect code",
            block.Contains("PayCost Clock"), block);
        Check("cardedit block encodes line breaks",
            block.Contains("Text 【自】第一行\\n第二行"), block);
        Check("cardedit block ends with EndCard",
            block.EndsWith("EndCard", StringComparison.Ordinal), block);

        CardEditorDraft eventDraft = CreateTestDraft("TST/01-E002");
        eventDraft.Category = CardCategory.Event;
        Check("cardedit event block drops Power",
            !CardDataWriter.BuildBlock(eventDraft).Contains("Power "),
            CardDataWriter.BuildBlock(eventDraft));

        // A climax carries neither Power nor a redundant Level 0 / Cost 0.
        CardEditorDraft climaxDraft = CreateTestDraft("TST/01-E003");
        climaxDraft.Category = CardCategory.Climax;
        climaxDraft.Level = 0;
        climaxDraft.Cost = 0;
        climaxDraft.Power = 0;
        climaxDraft.Backup = 0;
        string climaxBlock = CardDataWriter.BuildBlock(climaxDraft);
        Check("cardedit climax block drops Power",
            !climaxBlock.Contains("Power "), climaxBlock);
        Check("cardedit climax block drops Level 0",
            !climaxBlock.Contains("Level 0"), climaxBlock);
        Check("cardedit climax block drops Cost 0",
            !climaxBlock.Contains("Cost 0"), climaxBlock);
    }

    private static void TestCardEditorSave()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-cardedit-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            SeriesFolder folder = CreateTestFolder(root);

            // Two cards plus a comment line, CRLF, UTF-8 with BOM.
            CardEditorDraft first = CreateTestDraft("TST/01-E001");
            CardEditorDraft second = CreateTestDraft("TST/01-E002");
            second.Name = "第二张";

            string path = Path.Combine(root, "CardData.txt");
            string original = "// hand written comment\r\n" +
                CardDataWriter.BuildBlock(first).Replace("\n", "\r\n") +
                "\r\n\r\n" +
                CardDataWriter.BuildBlock(second).Replace("\n", "\r\n") + "\r\n";
            File.WriteAllText(path, original, new UTF8Encoding(true));

            List<WeissCard> before = ParseTestFolder(folder);
            Check("cardedit parses the fixture", before.Count == 2,
                before.Count.ToString());
            Check("cardedit reads the effect code",
                before.Count > 0 && before[0].EffectCode.Contains("PayCost Clock"),
                before.Count > 0 ? before[0].EffectCode : string.Empty);

            // 修改卡片: rename and change the stats of the first card.
            CardEditorDraft edit = CardEditorDraft.FromCard(before[0], folder);
            edit.Name = "改名后的卡";
            edit.Level = 3;
            edit.Power = 9000;
            edit.Trigger = TriggerIcon.DoubleSoul;
            edit.Text = "【起】新的效果";
            CardWriteResult saved = CardDataWriter.Save(edit);
            Check("cardedit save succeeds", saved.Success, saved.Message);
            Check("cardedit save keeps a backup", File.Exists(path + ".bak"),
                saved.BackupPath);

            string afterText = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit save keeps the comment",
                afterText.Contains("// hand written comment"), afterText);
            Check("cardedit save keeps the other card",
                afterText.Contains("Name 第二张"), afterText);
            Check("cardedit save keeps CRLF",
                afterText.Contains("Name 改名后的卡\r\n"), afterText);
            byte[] savedBytes = File.ReadAllBytes(path);
            Check("cardedit save keeps the BOM",
                savedBytes.Length > 3 && savedBytes[0] == 0xEF &&
                savedBytes[1] == 0xBB && savedBytes[2] == 0xBF,
                savedBytes.Length.ToString());

            List<WeissCard> after = ParseTestFolder(folder);
            Check("cardedit save keeps two cards", after.Count == 2,
                after.Count.ToString());
            WeissCard updated = after.Count > 0 ? after[0] : null;
            Check("cardedit save writes the fields",
                updated != null && updated.Name == "改名后的卡" &&
                updated.Level == 3 && updated.Power == 9000 &&
                updated.Trigger == TriggerIcon.DoubleSoul,
                updated == null ? "null" : updated.Name + "/" + updated.Level);
            Check("cardedit save keeps the effect code",
                updated != null && updated.EffectCode.Contains("PayCost Clock"),
                updated == null ? "null" : updated.EffectCode);
            Check("cardedit save keeps the new text",
                updated != null && updated.Text == "【起】新的效果",
                updated == null ? "null" : updated.Text);

            // 添加卡片: append a third block.
            CardEditorDraft add = CreateTestDraft("TST/01-E003");
            add.IsNew = true;
            add.FolderFullPath = root;
            add.FileName = "CardData";
            CardWriteResult added = CardDataWriter.Save(add);
            Check("cardedit add succeeds", added.Success && added.Added,
                added.Message);
            int addedCount = ParseTestFolder(folder).Count;
            Check("cardedit add appends a block", addedCount == 3,
                addedCount.ToString());

            // A duplicate card number is refused instead of overwriting.
            CardEditorDraft duplicate = CreateTestDraft("TST/01-E001");
            duplicate.IsNew = true;
            duplicate.FolderFullPath = root;
            duplicate.FileName = "CardData";
            CardWriteResult refused = CardDataWriter.Save(duplicate);
            Check("cardedit refuses a duplicate card number", !refused.Success,
                refused.Message);
            int afterRefusal = ParseTestFolder(folder).Count;
            Check("cardedit refused duplicate keeps the file", afterRefusal == 3,
                afterRefusal.ToString());

            // 修改卡片 on a card without a block (an AlternateArtwork clone)
            // must not append a second card behind the player's back.
            CardEditorDraft ghost = CreateTestDraft("TST/01-E099");
            ghost.OriginalId = "TST/01-E099";
            ghost.FolderFullPath = root;
            ghost.FileName = "CardData";
            CardWriteResult ghostResult = CardDataWriter.Save(ghost);
            Check("cardedit refuses a block for a missing card",
                !ghostResult.Success, ghostResult.Message);
            int afterGhost = ParseTestFolder(folder).Count;
            Check("cardedit missing card keeps the file", afterGhost == 3,
                afterGhost.ToString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// A card pack file is not always UTF-8 with BOM. Saving one card must not
    /// rewrite the encoding or the line endings of the cards around it.
    /// </summary>
    /// <summary>
    /// Rewriting a block only touches the edited fields: the shorthand, the
    /// Requirement line and the hand written text stay exactly as they were.
    /// </summary>
    private static void TestCardEditorPatch()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-cardpatch-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            SeriesFolder folder = CreateTestFolder(root);
            string path = Path.Combine(root, "CardData.txt");
            string original =
                "Character: TST/05-E001\r\n" +
                "Name 简写卡\r\n" +
                "JPName テスト\r\n" +
                "Color R\r\n" +
                "Level 1\r\n" +
                "Cost 0\r\n" +
                "Power 5000\r\n" +
                "Soul 1\r\n" +
                "Trait1 テスト\r\n" +
                "Combo テストコンボ\r\n" +
                "Requirement StageContains 1 IsColor Red\r\n" +
                "*1000Assist\r\n" +
                "Text 原文\r\n" +
                "EndCard\r\n";
            File.WriteAllText(path, original, new UTF8Encoding(false));

            List<WeissCard> cards = ParseTestFolder(folder);
            Check("cardedit patch parses the shorthand card", cards.Count == 1,
                cards.Count.ToString());
            WeissCard card = cards.Count > 0 ? cards[0] : null;
            Check("cardedit patch keeps the shorthand code",
                card != null && card.EffectCode.Contains("*1000Assist"),
                card == null ? "null" : card.EffectCode);
            Check("cardedit patch keeps the requirement",
                card != null && card.EffectCode.Contains("Requirement"),
                card == null ? "null" : card.EffectCode);
            Check("cardedit patch reads the combo",
                card != null && card.Combo == "テストコンボ",
                card == null ? "null" : card.Combo);

            CardEditorDraft draft = CardEditorDraft.FromCard(card, folder);
            draft.Name = "改名卡";
            draft.Level = 2;
            CardWriteResult saved = CardDataWriter.Save(draft);
            Check("cardedit patch save succeeds", saved.Success, saved.Message);

            string after = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit patch writes the new name",
                after.Contains("Name 改名卡"), after);
            Check("cardedit patch writes the new level",
                after.Contains("Level 2"), after);
            Check("cardedit patch keeps the shorthand line",
                after.Contains("*1000Assist"), after);
            Check("cardedit patch keeps the requirement line",
                after.Contains("Requirement StageContains 1 IsColor Red"), after);
            Check("cardedit patch keeps the combo line",
                after.Contains("Combo テストコンボ"), after);
            Check("cardedit patch keeps the text once",
                CountOccurrences(after, "Text 原文") == 1, after);
            Check("cardedit patch keeps the soul line once",
                CountOccurrences(after, "Soul 1") == 1, after);
            Check("cardedit patch keeps one card", ParseTestFolder(folder).Count == 1,
                after);

            // Renaming the 卡号 rewrites the header and nothing else.
            draft.Id = "TST/05-E002";
            CardWriteResult renamed = CardDataWriter.Save(draft);
            Check("cardedit patch rename succeeds", renamed.Success,
                renamed.Message);
            string header = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit patch rewrites the header",
                header.Contains("Character: TST/05-E002") &&
                !header.Contains("TST/05-E001"), header);
            Check("cardedit patch keeps a single card after renaming",
                ParseTestFolder(folder).Count == 1, header);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// 保存 must not turn the 效果内容 a `*Shorthand` copies in from
    /// CommonEffects.txt into a `Text` line. 编辑卡片 still shows that text for a
    /// card that prints no 效果内容 of its own, so the card face reads complete,
    /// but the block keeps only the shorthand and saving twice leaves the file
    /// exactly as it was.
    /// </summary>
    private static void TestCardEditorShorthandText()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-shorthand-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            SeriesFolder folder = CreateTestFolder(root);
            string path = Path.Combine(root, "CardData.txt");
            string commonPath = Path.Combine(root, "CommonEffects.txt");

            File.WriteAllText(commonPath,
                "Define: TestEncore\r\n" +
                "Auto: Encore Discard 1 IsCharacter\r\n" +
                "{\r\n" +
                "}\r\n" +
                "Text 【自】 简写效果 A\r\n" +
                "Tag Encore\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(path,
                "Character: TST/06-E001\r\n" +
                "Name 简写卡\r\n" +
                "JPName テスト\r\n" +
                "Color R\r\n" +
                "Level 1\r\n" +
                "Cost 0\r\n" +
                "Power 5000\r\n" +
                "Soul 1\r\n" +
                "Trait1 テスト\r\n" +
                "*TestEncore\r\n" +
                "EndCard\r\n",
                new UTF8Encoding(false));

            CommonEffectLibrary library = CommonEffectLibrary.Load(commonPath);
            WeissCard card = ParseTestFolder(folder, library)[0];
            Check("cardedit shorthand shows the inherited text",
                card.Text == "【自】 简写效果 A", card.Text);
            Check("cardedit shorthand remembers the inherited text",
                card.InheritedTextLines.Count == 1 &&
                card.InheritedTextLines[0] == "【自】 简写效果 A",
                card.InheritedTextLines.Count.ToString());

            CardEditorDraft draft = CardEditorDraft.FromCard(card, folder);
            Check("cardedit shorthand keeps the text in the editor",
                draft.TextLines != null && draft.TextLines.Count == 1 &&
                draft.TextLines[0] == "【自】 简写效果 A",
                draft.TextLines == null
                    ? "null"
                    : string.Join(" / ", draft.TextLines.ToArray()));

            draft.Level = 2;
            CardWriteResult saved = CardDataWriter.Save(draft);
            Check("cardedit shorthand save succeeds", saved.Success, saved.Message);

            string after = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit shorthand keeps the shorthand line",
                CountOccurrences(after, "*TestEncore") == 1, after);
            Check("cardedit shorthand writes no Text line",
                CountOccurrences(after, "Text ") == 0, after);
            Check("cardedit shorthand writes the edited level",
                after.Contains("Level 2"), after);

            WeissCard reloaded = ParseTestFolder(folder, library)[0];
            Check("cardedit shorthand still shows the text after a save",
                reloaded.Text == "【自】 简写效果 A", reloaded.Text);
            CardWriteResult again = CardDataWriter.Save(
                CardEditorDraft.FromCard(reloaded, folder));
            string twice = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit shorthand stays unchanged after a second save",
                again.Success && twice == after, twice);

            // Editing the 效果内容 itself must still reach the file.
            CardEditorDraft edited = CardEditorDraft.FromCard(
                ParseTestFolder(folder, library)[0], folder);
            edited.TextLines[0] = "玩家改写的效果";
            edited.Text = "玩家改写的效果";
            CardWriteResult editedSave = CardDataWriter.Save(edited);
            string rewritten = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit shorthand writes an edited text once",
                editedSave.Success &&
                CountOccurrences(rewritten, "Text 玩家改写的效果") == 1, rewritten);
            Check("cardedit shorthand keeps the shorthand after an edit",
                CountOccurrences(rewritten, "*TestEncore") == 1 &&
                CountOccurrences(rewritten, "简写效果") == 0, rewritten);

            // A block that already stored the expanded text heals on the next
            // save: the shorthand puts the same 效果 back on load.
            File.WriteAllText(path,
                "Character: TST/06-E001\r\n" +
                "Name 简写卡\r\n" +
                "Level 1\r\n" +
                "*TestEncore\r\n" +
                "Text 自己的效果\r\n" +
                "Text 【自】 简写效果 A\r\n" +
                "EndCard\r\n",
                new UTF8Encoding(false));
            CardWriteResult healed = CardDataWriter.Save(CardEditorDraft.FromCard(
                ParseTestFolder(folder, library)[0], folder));
            string cleaned = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit shorthand drops a duplicated inherited text",
                healed.Success && CountOccurrences(cleaned, "Text ") == 1 &&
                CountOccurrences(cleaned, "简写效果") == 0, cleaned);
            Check("cardedit shorthand keeps the own text when it heals",
                CountOccurrences(cleaned, "Text 自己的效果") == 1, cleaned);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// A card whose 效果 are written as `*Shorthand` lines keeps one row per
    /// shorthand: 编辑卡片 shows the `Text` line that belongs to that row, not
    /// the text the shorthand brings in as well. CardData.txt writes
    /// `*A` + `Text T1` + `*B` + `Text T2`, so an untouched card must write every
    /// `Text` line back below its own shorthand — no 效果 stored twice, nothing
    /// collected at the end of the block.
    /// </summary>
    private static void TestCardEditorShorthandPairs()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-shorthandpairs-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            SeriesFolder folder = CreateTestFolder(root);
            string path = Path.Combine(root, "CardData.txt");
            string commonPath = Path.Combine(root, "CommonEffects.txt");

            File.WriteAllText(commonPath,
                "Define: ShoutA\r\n" +
                "Auto: OnPlay\r\n" +
                "{\r\n" +
                "        GainPower 1000\r\n" +
                "}\r\n" +
                "Text 简写自带的文字 A\r\n" +
                "\r\n" +
                "Define: DrawB\r\n" +
                "Auto: OnAttack\r\n" +
                "{\r\n" +
                "        Draw 1\r\n" +
                "}\r\n" +
                "Text 简写自带的文字 B\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(path,
                "Character: TST/07-E001\r\n" +
                "Name 双简写卡\r\n" +
                "Color R\r\n" +
                "Level 1\r\n" +
                "*ShoutA\r\n" +
                "Text 卡面文字一\r\n" +
                "*DrawB\r\n" +
                "Text 卡面文字二\r\n" +
                "EndCard\r\n",
                new UTF8Encoding(false));

            CommonEffectLibrary library = CommonEffectLibrary.Load(commonPath);
            List<WeissCard> cards = ParseTestFolder(folder, library);
            Check("cardedit shorthand pairs parses one card", cards.Count == 1,
                cards.Count.ToString());
            if (cards.Count != 1)
                return;
            WeissCard card = cards[0];
            Check("cardedit shorthand pairs remembers both inherited texts",
                card.InheritedTextLines.Count == 2,
                card.InheritedTextLines.Count.ToString());
            Check("cardedit shorthand pairs keeps the file order of the text",
                card.TextLines.Count == 4 && card.TextLines[1] == "卡面文字一" &&
                card.TextLines[3] == "卡面文字二",
                string.Join(" / ", card.TextLines.ToArray()));

            CardEditorDraft draft = CardEditorDraft.FromCard(card, folder);
            IList<CardEffectPair> pairs = CardEffectSplitter.PairDraft(draft);
            Check("cardedit shorthand pairs shows one row per shorthand",
                pairs.Count == 2, pairs.Count.ToString());
            if (pairs.Count != 2)
                return;
            Check("cardedit shorthand pairs shows the own text of row 1",
                pairs[0].Text == "卡面文字一" &&
                pairs[0].Code.IndexOf("*ShoutA", StringComparison.Ordinal) >= 0,
                pairs[0].Text + " / " + pairs[0].Code);
            Check("cardedit shorthand pairs shows the own text of row 2",
                pairs[1].Text == "卡面文字二" &&
                pairs[1].Code.IndexOf("*DrawB", StringComparison.Ordinal) >= 0,
                pairs[1].Text + " / " + pairs[1].Code);

            // 改卡名：效果区一个字节都不该动。
            draft.Name = "改名卡";
            CardWriteResult saved = CardDataWriter.Save(draft);
            string after = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit shorthand pairs save succeeds", saved.Success,
                saved.Message);
            Check("cardedit shorthand pairs writes the new name",
                after.Contains("Name 改名卡"), after);
            Check("cardedit shorthand pairs keeps exactly two Text lines",
                CountOccurrences(after, "Text ") == 2, after);
            Check("cardedit shorthand pairs writes 内容 1 below 简写 1",
                IsBetween(after, "*ShoutA", "Text 卡面文字一", "*DrawB"), after);
            Check("cardedit shorthand pairs writes 内容 2 below 简写 2",
                IsAfter(after, "*DrawB", "Text 卡面文字二"), after);
            Check("cardedit shorthand pairs writes no inherited text",
                CountOccurrences(after, "简写自带的文字") == 0, after);

            CardWriteResult again = CardDataWriter.Save(
                CardEditorDraft.FromCard(ParseTestFolder(folder, library)[0], folder));
            string twice = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit shorthand pairs leaves the block alone on a second save",
                again.Success && twice == after, twice);

            // 只改第二条效果：第二条 Text 换掉，第一条留在自己的简写下面。
            CardEditorDraft edited = CardEditorDraft.FromCard(
                ParseTestFolder(folder, library)[0], folder);
            edited.TextLines = new List<string> { "卡面文字一", "卡面文字二改" };
            edited.Text = "卡面文字一\n卡面文字二改";
            CardWriteResult editResult = CardDataWriter.Save(edited);
            string rewritten = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit shorthand pairs writes the edited text once",
                editResult.Success &&
                CountOccurrences(rewritten, "Text 卡面文字二改") == 1, rewritten);
            Check("cardedit shorthand pairs keeps the other text in place",
                CountOccurrences(rewritten, "Text ") == 2 &&
                IsBetween(rewritten, "*ShoutA", "Text 卡面文字一", "*DrawB"),
                rewritten);
            Check("cardedit shorthand pairs keeps both shorthands once",
                CountOccurrences(rewritten, "*ShoutA") == 1 &&
                CountOccurrences(rewritten, "*DrawB") == 1, rewritten);
            Check("cardedit shorthand pairs writes no inherited text after an edit",
                CountOccurrences(rewritten, "简写自带的文字") == 0, rewritten);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>True when `first` ... `second` ... `third` appear in that
    /// order, so a `Text` line can be checked against the shorthand above and
    /// the one below it.</summary>
    private static bool IsBetween(string text, string first, string second,
        string third)
    {
        int a = text.IndexOf(first, StringComparison.Ordinal);
        int b = text.IndexOf(second, StringComparison.Ordinal);
        int c = text.IndexOf(third, StringComparison.Ordinal);
        return a >= 0 && b > a && c > b;
    }

    /// <summary>True when `first` appears before `second`.</summary>
    private static bool IsAfter(string text, string first, string second)
    {
        int a = text.IndexOf(first, StringComparison.Ordinal);
        int b = text.IndexOf(second, StringComparison.Ordinal);
        return a >= 0 && b > a;
    }

    /// <summary>
    /// 一条效果写成 `*Shorthand` + `Text` 时，编辑卡片的中栏要同时给出这两块
    /// 文字：简写从 CommonEffects.txt 带进来的正文（只读预览）和卡块自己写的
    /// `Text` 行（可编辑）。以前只取“自己的”文字，简写那一段的内容框于是空白，
    /// 后面的效果还整体往前挪一格，保存也跟着写错位。
    /// </summary>
    private static void TestCardEditorShorthandRow()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-shorthandrow-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            SeriesFolder folder = CreateTestFolder(root);
            string path = Path.Combine(root, "CardData.txt");
            string commonPath = Path.Combine(root, "CommonEffects.txt");

            File.WriteAllText(commonPath,
                "Define: EncoreAll\r\n" +
                "Auto: OnAttack\r\n" +
                "{\r\n" +
                "        GainPower 2000\r\n" +
                "}\r\n" +
                "Text 【自】简写正文一\r\n" +
                "Text 【自】简写正文二\r\n" +
                "\r\n" +
                "Define: HealOne\r\n" +
                "Act: Rest\r\n" +
                "{\r\n" +
                "        Heal 1\r\n" +
                "}\r\n" +
                "Text 【起】简写正文三\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(path,
                "Character: TST/08-E001\r\n" +
                "Name 简写行卡\r\n" +
                "Level 1\r\n" +
                "*EncoreAll\r\n" +
                "Text 自己的正文一\r\n" +
                "*HealOne\r\n" +
                "Auto: OnPlay\r\n" +
                "{\r\n" +
                "        Draw 1\r\n" +
                "}\r\n" +
                "Text 自动的正文\r\n" +
                "EndCard\r\n",
                new UTF8Encoding(false));

            CommonEffectLibrary library = CommonEffectLibrary.Load(commonPath);
            List<WeissCard> cards = ParseTestFolder(folder, library);
            Check("cardedit shorthand row parses one card", cards.Count == 1,
                cards.Count.ToString());
            if (cards.Count != 1)
                return;
            WeissCard card = cards[0];

            Check("cardedit shorthand row keeps the file order of the text",
                card.TextLines.Count == 5 &&
                card.TextLines[0] == "【自】简写正文一" &&
                card.TextLines[2] == "自己的正文一" &&
                card.TextLines[3] == "【起】简写正文三" &&
                card.TextLines[4] == "自动的正文",
                string.Join(" / ", card.TextLines.ToArray()));
            Check("cardedit shorthand row marks the inherited text",
                card.TextInherited.Count == 5 &&
                card.TextInherited[0] && card.TextInherited[1] &&
                !card.TextInherited[2] && card.TextInherited[3] &&
                !card.TextInherited[4],
                string.Join(",", card.TextInherited.ToArray()));
            Check("cardedit shorthand row numbers the blocks",
                card.TextBlockIndexes.Count == 5 &&
                card.TextBlockIndexes[0] == 0 && card.TextBlockIndexes[2] == 0 &&
                card.TextBlockIndexes[3] == 1 && card.TextBlockIndexes[4] == 2,
                string.Join(",", card.TextBlockIndexes.ToArray()));

            CardEditorDraft draft = CardEditorDraft.FromCard(card, folder);
            IList<CardEffectPair> pairs = CardEffectSplitter.PairDraft(draft);
            Check("cardedit shorthand row shows one row per 效果",
                pairs.Count == 3, pairs.Count.ToString());
            if (pairs.Count != 3)
                return;
            Check("cardedit shorthand row shows the own text of row 1",
                pairs[0].Text == "自己的正文一", pairs[0].Text);
            Check("cardedit shorthand row shows the inherited text of row 1",
                pairs[0].InheritedText == "【自】简写正文一\n【自】简写正文二",
                pairs[0].InheritedText);
            Check("cardedit shorthand row shows the inherited text of row 2",
                pairs[1].InheritedText == "【起】简写正文三", pairs[1].InheritedText);
            Check("cardedit shorthand row keeps row 2 without own text",
                pairs[1].Text == string.Empty, pairs[1].Text);
            Check("cardedit shorthand row keeps 内容 3 in row 3",
                pairs[2].Text == "自动的正文" &&
                pairs[2].InheritedText == string.Empty,
                pairs[2].Text + " / " + pairs[2].InheritedText);
            Check("cardedit shorthand row keeps the shorthand in its own row",
                pairs[0].Code == "*EncoreAll" && pairs[1].Code == "*HealOne" &&
                pairs[2].Code.StartsWith("Auto: OnPlay", StringComparison.Ordinal),
                pairs[0].Code + " / " + pairs[1].Code + " / " + pairs[2].Code);

            List<string> own = CardEffectSplitter.OwnTextLines(draft);
            Check("cardedit shorthand row writes only the own text",
                string.Join("|", own.ToArray()) == "自己的正文一|自动的正文",
                string.Join("|", own.ToArray()));

            // 改卡名：两块自有文字都要留在自己的代码下面，简写正文一个字都不写回。
            draft.Name = "改名卡";
            CardWriteResult saved = CardDataWriter.Save(draft);
            string after = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit shorthand row save succeeds", saved.Success, saved.Message);
            Check("cardedit shorthand row writes the new name",
                after.Contains("Name 改名卡"), after);
            Check("cardedit shorthand row keeps two Text lines",
                CountOccurrences(after, "Text ") == 2, after);
            Check("cardedit shorthand row writes 内容 1 below 简写 1",
                IsBetween(after, "*EncoreAll", "Text 自己的正文一", "*HealOne"), after);
            Check("cardedit shorthand row writes 内容 3 below 代码 3",
                IsAfter(after, "Auto: OnPlay", "Text 自动的正文"), after);
            Check("cardedit shorthand row writes no inherited text",
                CountOccurrences(after, "简写正文") == 0, after);

            CardWriteResult again = CardDataWriter.Save(
                CardEditorDraft.FromCard(ParseTestFolder(folder, library)[0], folder));
            string twice = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit shorthand row leaves the block alone on a second save",
                again.Success && twice == after, twice);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// A climax shorthand carries tags of its own: `*2SoulClimax` in
    /// CommonEffects.txt writes `Trigger DoubleSoul` and `*RedStockSoul` writes
    /// `Color R`. 编辑卡片 shows those values, but 保存 must not spell them out
    /// inside the block — the shorthand writes them again on every load, so the
    /// card would carry the same trigger (or colour) twice. A tag the player
    /// really picks still becomes a line of its own.
    /// </summary>
    private static void TestCardEditorClimaxShorthand()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-climaxshorthand-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            SeriesFolder folder = CreateTestFolder(root);
            string path = Path.Combine(root, "CardData.txt");
            string commonPath = Path.Combine(root, "CommonEffects.txt");

            File.WriteAllText(commonPath,
                "Define: 2SoulClimax\r\n" +
                "\tTrigger DoubleSoul\r\n" +
                "\tTag Climax_2Soul\r\n" +
                "\tCont: GiveAll\r\n" +
                "\t{\r\n" +
                "\t\tGainSoul 2\r\n" +
                "\t}\r\n" +
                "\tText 【永】你所有的角色魂值 +2 。\r\n" +
                "\r\n" +
                "Define: RedStockSoul\r\n" +
                "\tColor R\r\n" +
                "\tTrigger DoubleSoul\r\n" +
                "\tAuto: OnPlay\r\n" +
                "\t{\r\n" +
                "\t\tMoveCard Stock\r\n" +
                "\t}\r\n" +
                "\tText 【自】你控室1枚红色的卡放到库存区。\r\n",
                new UTF8Encoding(false));

            string original =
                "Climax: TST/08-E001\r\n" +
                "Name 二魂高潮\r\n" +
                "Color Y\r\n" +
                "*2SoulClimax\r\n" +
                "EndCard\r\n" +
                "\r\n" +
                "Climax: TST/08-E002\r\n" +
                "Name 红色高潮\r\n" +
                "*RedStockSoul\r\n" +
                "EndCard\r\n";
            File.WriteAllText(path, original, new UTF8Encoding(false));

            CommonEffectLibrary library = CommonEffectLibrary.Load(commonPath);
            List<WeissCard> cards = ParseTestFolder(folder, library);
            Check("cardedit climax shorthand parses both cards", cards.Count == 2,
                cards.Count.ToString());
            WeissCard soul = cards[0];
            WeissCard red = cards[1];
            Check("cardedit climax shorthand shows the inherited trigger",
                soul.Trigger == TriggerIcon.DoubleSoul &&
                soul.InheritedTrigger == TriggerIcon.DoubleSoul,
                soul.Trigger + " / " + soul.InheritedTrigger);
            Check("cardedit climax shorthand shows the inherited colour",
                red.Color == CardColor.Red && red.InheritedColor == CardColor.Red,
                red.Color + " / " + red.InheritedColor);

            CardWriteResult saved = CardDataWriter.Save(
                CardEditorDraft.FromCard(soul, folder));
            CardWriteResult savedRed = CardDataWriter.Save(
                CardEditorDraft.FromCard(red, folder));
            string after = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit climax shorthand save succeeds",
                saved.Success && savedRed.Success,
                saved.Message + " / " + savedRed.Message);
            Check("cardedit climax shorthand writes no trigger line",
                CountOccurrences(after, "Trigger ") == 0, after);
            Check("cardedit climax shorthand writes no colour line",
                Array.IndexOf(ReadCardBlock(after, "TST/08-E002"), "Color B") < 0,
                string.Join(" | ", ReadCardBlock(after, "TST/08-E002")));
            Check("cardedit climax shorthand writes no Text line",
                CountOccurrences(after, "Text ") == 0, after);
            Check("cardedit climax shorthand stays byte identical",
                after == original, after);

            // A trigger the player picks still becomes a line of its own.
            CardEditorDraft picked = CardEditorDraft.FromCard(soul, folder);
            Check("cardedit climax shorthand draft keeps the inherited trigger",
                picked.Trigger == TriggerIcon.DoubleSoul &&
                picked.InheritedTrigger == TriggerIcon.DoubleSoul,
                picked.Trigger + " / " + picked.InheritedTrigger);
            picked.Trigger = TriggerIcon.Book;
            CardWriteResult pickSave = CardDataWriter.Save(picked);
            string chosen = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit climax shorthand writes the picked trigger",
                pickSave.Success && CountOccurrences(chosen, "Trigger Book") == 1,
                chosen);
            Check("cardedit climax shorthand keeps the shorthand after the edit",
                CountOccurrences(chosen, "*2SoulClimax") == 1, chosen);

            WeissCard reloaded = ParseTestFolder(folder, library)[0];
            Check("cardedit climax shorthand reloads the picked trigger",
                reloaded.Trigger == TriggerIcon.Book,
                reloaded.Trigger.ToString());
            CardWriteResult again = CardDataWriter.Save(
                CardEditorDraft.FromCard(reloaded, folder));
            string twice = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit climax shorthand writes the trigger once",
                again.Success && twice == chosen, twice);

            // A colour the shorthand sets behaves the same way.
            CardEditorDraft colour = CardEditorDraft.FromCard(
                ParseTestFolder(folder, library)[1], folder);
            colour.Color = CardColor.Blue;
            CardWriteResult colourSave = CardDataWriter.Save(colour);
            string colourFile = File.ReadAllText(path, Encoding.UTF8);
            string[] colourBlock = ReadCardBlock(colourFile, "TST/08-E002");
            Check("cardedit climax shorthand writes the picked colour",
                colourSave.Success && Array.IndexOf(colourBlock, "Color B") >= 0,
                string.Join(" | ", colourBlock));
            WeissCard colourReload = ParseTestFolder(folder, library)[1];
            Check("cardedit climax shorthand reloads the picked colour",
                colourReload.Color == CardColor.Blue,
                colourReload.Color.ToString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// 保存 of an alt-art card must leave its `Clone xxx` block alone: 编辑卡片
    /// shows the base card's fields and 效果, but the block keeps just the Clone
    /// line until the player really changes a value.
    /// </summary>
    private static void TestCardEditorCloneBlock()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-cloneblock-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            SeriesFolder folder = CreateTestFolder(root);
            string path = Path.Combine(root, "CardData.txt");
            const string original =
                "Character: TST/07-E001\r\n" +
                "Name 基卡\r\n" +
                "JPName キカード\r\n" +
                "Color R\r\n" +
                "Level 1\r\n" +
                "Cost 0\r\n" +
                "Power 6000\r\n" +
                "Soul 1\r\n" +
                "Trait1 テスト\r\n" +
                "Auto: OnPlay\r\n" +
                "{\r\n" +
                "        Draw 1\r\n" +
                "}\r\n" +
                "Text 基卡效果\r\n" +
                "EndCard\r\n" +
                "\r\n" +
                "Character: TST/07-E002\r\n" +
                "Clone TST/07-E001\r\n" +
                "EndCard\r\n";
            File.WriteAllText(path, original, new UTF8Encoding(false));

            List<WeissCard> cards = ParseTestFolder(folder);
            Check("cardedit clone block parses both cards", cards.Count == 2,
                cards.Count.ToString());
            WeissCard clone = cards.Count > 1 ? cards[1] : null;
            Check("cardedit clone block inherits the base effect in the editor",
                clone != null && clone.EffectCode.Contains("Draw 1"),
                clone == null ? "null" : clone.EffectCode);
            Check("cardedit clone block inherits the base text in the editor",
                clone != null && clone.Text == "基卡效果",
                clone == null ? "null" : clone.Text);

            CardWriteResult saved = CardDataWriter.Save(
                CardEditorDraft.FromCard(clone, folder));
            Check("cardedit clone block save succeeds", saved.Success,
                saved.Message);

            string after = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit clone block stays three lines",
                ReadCardBlock(after, "TST/07-E002").Length == 3, after);
            Check("cardedit clone block stays byte identical", after == original,
                after);
            Check("cardedit clone block writes no inherited effect",
                CountOccurrences(after, "Draw 1") == 1 &&
                CountOccurrences(after, "Text ") == 1 &&
                CountOccurrences(after, "Name 基卡") == 1, after);

            // A player edit still reaches the block, and only that field does.
            CardEditorDraft edited = CardEditorDraft.FromCard(
                ParseTestFolder(folder)[1], folder);
            edited.Level = 2;
            CardWriteResult changedSave = CardDataWriter.Save(edited);
            string changedFile = File.ReadAllText(path, Encoding.UTF8);
            string[] changedBlock = ReadCardBlock(changedFile, "TST/07-E002");
            Check("cardedit clone block writes only the edited field",
                changedSave.Success && changedBlock.Length == 4 &&
                changedBlock[2] == "Level 2",
                string.Join(" | ", changedBlock));
            Check("cardedit clone block keeps inheriting after an edit",
                CountOccurrences(changedFile, "Draw 1") == 1 &&
                CountOccurrences(changedFile, "Name 基卡") == 1, changedFile);

            WeissCard reloaded = ParseTestFolder(folder)[1];
            Check("cardedit clone block reloads the edit",
                reloaded.Level == 2 && reloaded.Text == "基卡效果",
                reloaded.Level + " / " + reloaded.Text);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// The alt-art cards of a real pack keep their `Clone xxx` block when the
    /// editor saves them unchanged. The CardData file is copied to a temp
    /// folder first, so the game data is only ever read.
    /// </summary>
    private static void TestRealCloneBlocks(SeriesDefinition series)
    {
        if (series == null)
            return;

        string dataRoot = DataPathResolver.Resolve(null);
        if (string.IsNullOrEmpty(dataRoot))
        {
            Check("cardedit real clone block finds the game data", false,
                "no data root");
            return;
        }

        CommonEffectLibrary library = CommonEffectLibrary.Load(
            Path.Combine(dataRoot, "CommonEffects.txt"));
        int tested = 0;
        foreach (WeissCard card in series.EnumerateCards())
        {
            if (tested >= 3)
                break;
            if (!card.IsClone || string.IsNullOrEmpty(card.FolderPath))
                continue;

            string folderPath = Path.Combine(dataRoot, "Cards",
                card.FolderPath.Replace('/', Path.DirectorySeparatorChar));
            string path = CardDataWriter.ResolveCardFilePath(folderPath,
                "CardData");
            if (!File.Exists(path))
                continue;

            string source = File.ReadAllText(path, Encoding.UTF8);
            string[] originalBlock = ReadCardBlock(source, card.Id);
            if (originalBlock.Length != 3)
                continue;   // only the plain header / Clone / EndCard blocks

            string root = Path.Combine(Path.GetTempPath(),
                "ws-realclone-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                string copy = Path.Combine(root, "CardData.txt");
                File.Copy(path, copy, true);

                var folder = new SeriesFolder();
                folder.SeriesName = card.SeriesName;
                folder.SetName = card.SetName;
                folder.FolderPath = card.FolderPath;
                folder.FullPath = root;
                folder.FileName = "CardData";

                List<WeissCard> parsed = ParseTestFolder(folder, library);
                WeissCard draftCard = null;
                for (int j = 0; j < parsed.Count && draftCard == null; j++)
                {
                    if (string.Equals(parsed[j].Id, card.Id,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        draftCard = parsed[j];
                    }
                }
                if (draftCard == null)
                    continue;

                CardWriteResult result = CardDataWriter.Save(
                    CardEditorDraft.FromCard(draftCard, folder));
                string[] afterBlock = ReadCardBlock(
                    File.ReadAllText(copy, Encoding.UTF8), card.Id);
                Check("cardedit real clone block keeps " + card.Id,
                    result.Success &&
                    string.Equals(string.Join("\n", afterBlock),
                        string.Join("\n", originalBlock), StringComparison.Ordinal),
                    result.Message + " / " + string.Join(" | ", afterBlock));
                tested++;
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        Check("cardedit real clone block tested some cards", tested > 0,
            tested.ToString());
    }

    /// <summary>
    /// 保存一张用 `*Shorthand` 写的真实卡，不改效果时卡块逐行不变：简写从
    /// CommonEffects.txt 带进来的文字不会被写进 `Text` 行，每条 `Text` 也留在
    /// 自己那条简写下面，第二次保存同样不动一个字节。CardData 文件先复制到
    /// 临时目录，游戏数据只读。
    /// </summary>
    private static void TestRealShorthandBlocks(SeriesDefinition series)
    {
        if (series == null)
            return;

        string dataRoot = DataPathResolver.Resolve(null);
        if (string.IsNullOrEmpty(dataRoot))
        {
            Check("cardedit real shorthand block finds the game data", false,
                "no data root");
            return;
        }

        CommonEffectLibrary library = CommonEffectLibrary.Load(
            Path.Combine(dataRoot, "CommonEffects.txt"));
        int tested = 0;
        foreach (WeissCard card in series.EnumerateCards())
        {
            if (tested >= 3)
                break;
            if (card.IsClone || string.IsNullOrEmpty(card.FolderPath) ||
                string.IsNullOrEmpty(card.EffectCode))
            {
                continue;
            }

            string folderPath = Path.Combine(dataRoot, "Cards",
                card.FolderPath.Replace('/', Path.DirectorySeparatorChar));
            string path = CardDataWriter.ResolveCardFilePath(folderPath,
                "CardData");
            if (!File.Exists(path))
                continue;

            string[] originalBlock = ReadCardBlock(
                File.ReadAllText(path, Encoding.UTF8), card.Id);
            string originalEffects = string.Join("\n", EffectLines(originalBlock));
            if (!HasShorthandText(originalEffects))
                continue;

            string root = Path.Combine(Path.GetTempPath(),
                "ws-realshorthand-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                string copy = Path.Combine(root, "CardData.txt");
                File.Copy(path, copy, true);

                var folder = new SeriesFolder();
                folder.SeriesName = card.SeriesName;
                folder.SetName = card.SetName;
                folder.FolderPath = card.FolderPath;
                folder.FullPath = root;
                folder.FileName = "CardData";

                WeissCard draftCard = FindTestCard(
                    ParseTestFolder(folder, library), card.Id);
                if (draftCard == null)
                    continue;

                CardWriteResult result = CardDataWriter.Save(
                    CardEditorDraft.FromCard(draftCard, folder));
                string afterFirst = File.ReadAllText(copy, Encoding.UTF8);
                string[] afterBlock = ReadCardBlock(afterFirst, card.Id);
                Check("cardedit real shorthand block keeps " + card.Id,
                    result.Success &&
                    string.Equals(originalEffects,
                        string.Join("\n", EffectLines(afterBlock)),
                        StringComparison.Ordinal),
                    result.Message + " / " +
                    string.Join(" | ", EffectLines(afterBlock)));

                WeissCard reloaded = FindTestCard(
                    ParseTestFolder(folder, library), card.Id);
                if (reloaded == null)
                    continue;
                CardWriteResult again = CardDataWriter.Save(
                    CardEditorDraft.FromCard(reloaded, folder));
                string afterSecond = File.ReadAllText(copy, Encoding.UTF8);
                Check("cardedit real shorthand block stays put on a second save " +
                    card.Id, again.Success && afterSecond == afterFirst,
                    again.Success ? "file changed" : again.Message);
                tested++;
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        Check("cardedit real shorthand block tested some cards", tested > 0,
            tested.ToString());
    }

    /// <summary>Card with the given id, or null.</summary>
    private static WeissCard FindTestCard(IList<WeissCard> cards, string id)
    {
        if (cards == null)
            return null;
        for (int i = 0; i < cards.Count; i++)
        {
            if (string.Equals(cards[i].Id, id, StringComparison.OrdinalIgnoreCase))
                return cards[i];
        }
        return null;
    }

    /// <summary>
    /// 效果区 of a card block: every line 编辑卡片 does not own as a field, so
    /// `Text` lines, `*Shorthand` lines and the `{ }` bodies stay in it.
    /// </summary>
    private static List<string> EffectLines(string[] block)
    {
        var lines = new List<string>();
        if (block == null)
            return lines;
        for (int i = 0; i < block.Length; i++)
        {
            string line = (block[i] ?? string.Empty).TrimEnd();
            if (IsTestFieldLine(line))
                continue;
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>
    /// True for a `Key value` line of a field the editor owns besides 效果内容
    /// (`Text` counts as 效果内容 and stays in the 效果区).
    /// </summary>
    private static bool IsTestFieldLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 ||
            trimmed.StartsWith("*", StringComparison.Ordinal))
        {
            return false;
        }

        int space = trimmed.IndexOf(' ');
        string token = (space < 0 ? trimmed : trimmed.Substring(0, space))
            .ToLowerInvariant();
        switch (token)
        {
            case "name":
            case "jpname":
            case "image":
            case "color":
            case "level":
            case "cost":
            case "power":
            case "soul":
            case "trigger":
            case "backup":
            case "trait1":
            case "trait2":
            case "trait3":
            case "combo":
                return true;
        }
        return false;
    }

    /// <summary>True when the 效果区 holds a `*Shorthand` and a `Text` line.</summary>
    private static bool HasShorthandText(string effects)
    {
        bool shorthand = false;
        bool text = false;
        string[] lines = CardDataWriter.SplitLines(effects);
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = (lines[i] ?? string.Empty).Trim();
            if (trimmed.StartsWith("*", StringComparison.Ordinal))
                shorthand = true;
            else if (trimmed.StartsWith("Text", StringComparison.OrdinalIgnoreCase))
                text = true;
        }
        return shorthand && text;
    }

    /// <summary>
    /// Every card of a real pack keeps its 效果 when the editor pairs 效果内容
    /// with 效果代码: no ability line is dropped, added or reordered, and the
    /// 效果内容 keeps every `Text` line the block spells out — whatever mixture
    /// of `Text` lines, `*Shorthand` lines and hidden trigger abilities the card
    /// carries. The text a `*Shorthand` copies in sits on its own row as the
    /// read-only 简写提供 preview, so a shorthand-only 效果 no longer leaves its
    /// 内容 box blank and pushes every later 效果 one row up.
    /// </summary>
    private static void TestRealEffectPairs(SeriesDefinition series)
    {
        int cards = 0;
        int multiRow = 0;
        int shorthandRows = 0;
        string broken = null;

        for (int f = 0; f < series.Folders.Count && broken == null; f++)
        {
            SeriesFolder folder = series.Folders[f];
            for (int c = 0; c < folder.Cards.Count && broken == null; c++)
            {
                WeissCard card = folder.Cards[c];
                if (card.EffectCode.Length == 0 && card.TextLines.Count == 0)
                    continue;

                CardEditorDraft draft = CardEditorDraft.FromCard(card, folder);
                IList<CardEffectPair> pairs = CardEffectSplitter.PairDraft(draft);
                cards++;
                if (pairs.Count > 1)
                    multiRow++;
                for (int p = 0; p < pairs.Count; p++)
                {
                    if (pairs[p].FromShorthand)
                        shorthandRows++;
                }

                // 编辑卡片 shows the card face complete: the text the block
                // prints itself plus the text its `*Shorthand` lines bring in.
                string expected = string.Join("\n", card.TextLines.ToArray());
                string texts = JoinPairParts(pairs, true);
                if (!SameLines(texts, expected))
                {
                    broken = card.Id + " 内容: " + texts + " != " + expected;
                    break;
                }

                string code = JoinPairParts(pairs, false);
                if (!SameLines(code, card.EffectCode))
                {
                    broken = card.Id + " 代码: " + code + " != " + card.EffectCode;
                    break;
                }
            }
        }

        Check("cardedit pairs every real card without losing a line",
            broken == null && cards > 100,
            (broken ?? "") + " cards=" + cards);
        Check("cardedit pairs splits real multi-effect cards into rows",
            multiRow > 0, cards + " cards / " + multiRow + " multi");
        Check("cardedit pairs shows the shorthand text of real cards",
            shorthandRows > 0, cards + " cards / " + shorthandRows + " shorthand rows");
    }

    private static string JoinPairParts(IList<CardEffectPair> pairs, bool texts)
    {
        var parts = new List<string>();
        for (int i = 0; i < pairs.Count; i++)
        {
            // CardData.txt writes the text of a `*Shorthand` right below the
            // shorthand and the card's own `Text` line after it, so the
            // preview comes first inside the row.
            if (texts && !string.IsNullOrEmpty(pairs[i].InheritedText))
                parts.Add(pairs[i].InheritedText);
            string value = texts ? pairs[i].Text : pairs[i].Code;
            if (!string.IsNullOrEmpty(value))
                parts.Add(value);
        }
        return string.Join("\n", parts.ToArray());
    }

    /// <summary>Same lines after trimming: the check is about order and
    /// content, not about the line endings an editor box produces.</summary>
    private static bool SameLines(string left, string right)
    {
        return string.Equals(FlattenLines(left), FlattenLines(right),
            StringComparison.Ordinal);
    }

    private static string FlattenLines(string text)
    {
        string[] lines = CardDataWriter.SplitLines(text);
        var parts = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length > 0)
                parts.Add(line);
        }
        return string.Join("\n", parts.ToArray());
    }

    /// <summary>
    /// 一条效果 = 效果内容 + 它自己的效果代码. CardData.txt writes 内容 1 /
    /// 代码 1 / 内容 2 / 代码 2, so the splitter must pair them that way and a
    /// save must not collect every `Text` line at the end of the block.
    /// </summary>
    private static void TestCardEffectPairs()
    {
        const string twoAbilities =
            "Auto: OnPlay\r\n" +
            "{\r\n" +
            "        Draw 1\r\n" +
            "}\r\n" +
            "Cont: GiveSelf\r\n" +
            "{\r\n" +
            "        GainPower 1000\r\n" +
            "}\r\n";

        List<string[]> segments = CardEffectSplitter.SplitCode(twoAbilities);
        Check("cardedit splitter finds both abilities", segments.Count == 2,
            segments.Count.ToString());

        IList<CardEffectPair> pairs =
            CardEffectSplitter.Pair("第一条\n第二条", twoAbilities);
        Check("cardedit splitter pairs each 内容 with its own 代码",
            pairs.Count == 2 && pairs[0].Text == "第一条" &&
            pairs[0].Code.Contains("Draw 1") && pairs[1].Text == "第二条" &&
            pairs[1].Code.Contains("GainPower 1000") && !pairs[1].Code.Contains("Draw 1"),
            pairs.Count + " / " + (pairs.Count > 1 ? pairs[1].Code : "?"));

        // A text that wraps on the card is one 效果, not the next one.
        IList<CardEffectPair> wrapped = CardEffectSplitter.Pair("第一行\n第二行",
            "Auto: OnPlay\r\n{\r\n}\r\n");
        Check("cardedit splitter keeps a wrapped text in one 效果",
            wrapped.Count == 1 && wrapped[0].Text == "第一行\n第二行",
            wrapped.Count + " / " + (wrapped.Count > 0 ? wrapped[0].Text : "?"));

        TestCardEffectPairSave();
    }

    /// <summary>
    /// Editing one text of a multi-effect card keeps every `Text` line under
    /// its own ability; only the edited line changes.
    /// </summary>
    private static void TestCardEffectPairSave()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-cardpairs-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            SeriesFolder folder = CreateTestFolder(root);
            string path = Path.Combine(root, "CardData.txt");
            File.WriteAllText(path,
                "Character: TST/06-E001\r\n" +
                "Name 两条效果\r\n" +
                "Color R\r\n" +
                "Level 1\r\n" +
                "Cost 0\r\n" +
                "Power 5000\r\n" +
                "Trait1 テスト\r\n" +
                "Auto: OnPlay\r\n" +
                "{\r\n" +
                "        Draw 1\r\n" +
                "}\r\n" +
                "Text 第一条\r\n" +
                "Cont: GiveSelf\r\n" +
                "{\r\n" +
                "        GainPower 1000\r\n" +
                "}\r\n" +
                "Text 第二条\r\n" +
                "EndCard\r\n",
                new UTF8Encoding(false));

            List<WeissCard> cards = ParseTestFolder(folder);
            WeissCard card = cards.Count > 0 ? cards[0] : null;
            Check("cardedit pairs keeps both text pieces",
                card != null && card.TextLines.Count == 2 &&
                card.TextLines[1] == "第二条",
                card == null ? "null" : card.TextLines.Count.ToString());

            CardEditorDraft draft = CardEditorDraft.FromCard(card, folder);
            IList<CardEffectPair> draftPairs = CardEffectSplitter.PairDraft(draft);
            Check("cardedit pairs reads the block as two 效果",
                draftPairs.Count == 2 && draftPairs[1].Text == "第二条" &&
                draftPairs[1].Code.Contains("GainPower 1000"),
                draftPairs.Count + " / " +
                (draftPairs.Count > 1 ? draftPairs[1].Text : "?"));

            draft.Name = "改名卡";
            Check("cardedit pairs save succeeds", CardDataWriter.Save(draft).Success);

            string after = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit pairs keeps the name change",
                after.Contains("Name 改名卡"), after);
            Check("cardedit pairs keeps 内容 1 below 代码 1",
                after.IndexOf("Text 第一条", StringComparison.Ordinal) >
                after.IndexOf("Draw 1", StringComparison.Ordinal) &&
                after.IndexOf("Text 第一条", StringComparison.Ordinal) <
                after.IndexOf("Cont: GiveSelf", StringComparison.Ordinal), after);
            Check("cardedit pairs keeps 内容 2 below 代码 2",
                after.IndexOf("Text 第二条", StringComparison.Ordinal) >
                after.IndexOf("GainPower 1000", StringComparison.Ordinal), after);
            Check("cardedit pairs keeps two text lines",
                CountOccurrences(after, "Text ") == 2, after);

            // Editing the second text keeps it under the second ability.
            draft = CardEditorDraft.FromCard(ParseTestFolder(folder)[0], folder);
            draft.TextLines = new List<string> { "第一条", "第二条改" };
            draft.Text = "第一条\n第二条改";
            Check("cardedit pairs text save succeeds",
                CardDataWriter.Save(draft).Success);

            after = File.ReadAllText(path, Encoding.UTF8);
            Check("cardedit pairs writes the edited text under its own 代码",
                after.Contains("Text 第二条改") &&
                after.IndexOf("Text 第一条", StringComparison.Ordinal) <
                after.IndexOf("Cont: GiveSelf", StringComparison.Ordinal) &&
                after.IndexOf("Text 第二条改", StringComparison.Ordinal) >
                after.IndexOf("GainPower 1000", StringComparison.Ordinal), after);
            Check("cardedit pairs keeps two text lines after the edit",
                CountOccurrences(after, "Text ") == 2, after);

            List<WeissCard> reloaded = ParseTestFolder(folder);
            Check("cardedit pairs keeps both 效果 after the edit",
                reloaded.Count == 1 && reloaded[0].TextLines.Count == 2 &&
                reloaded[0].TextLines[1] == "第二条改",
                reloaded.Count == 0 ? "none" : reloaded[0].TextLines.Count.ToString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// The 参考 panel searches 卡名 / 卡号 in one box and the effect text or the
    /// ability code in the other one, across every pack.
    /// </summary>
    private static void TestCardReferenceIndex()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-cardref-" + Guid.NewGuid().ToString("N"));
        try
        {
            string folderA = Path.Combine(root, "系列A", "S01");
            string folderB = Path.Combine(root, "系列B", "W02");
            Directory.CreateDirectory(folderA);
            Directory.CreateDirectory(folderB);
            File.WriteAllText(Path.Combine(folderA, "CardData.txt"),
                "Character: A/S01-001\r\n" +
                "Name 火の勇者\r\n" +
                "Level 1\r\n" +
                "Cost 0\r\n" +
                "Power 5000\r\n" +
                "Text 你的角色力量+1000\r\n" +
                "EndCard\r\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folderB, "CardData.txt"),
                "Event: B/W02-010\r\n" +
                "Name 氷の魔法\r\n" +
                "Level 2\r\n" +
                "Cost 1\r\n" +
                "Text 你抽1张卡\r\n" +
                "EndCard\r\n", new UTF8Encoding(false));

            var folders = new List<CardReferenceFolder>();
            var first = new CardReferenceFolder();
            first.SeriesName = "系列A";
            first.SetName = "S01";
            first.FolderPath = "系列A/S01";
            first.FolderFullPath = folderA;
            first.FileName = "CardData";
            folders.Add(first);

            var second = new CardReferenceFolder();
            second.SeriesName = "系列B";
            second.SetName = "W02";
            second.FolderPath = "系列B/W02";
            second.FolderFullPath = folderB;
            second.FileName = "CardData";
            folders.Add(second);

            CardReferenceIndex index = CardReferenceIndex.Build(folders,
                CommonEffectLibrary.Empty, null, null);
            Check("cardref reads both packs", index.Count == 2,
                index.Count.ToString());

            List<CardReferenceEntry> byCode = index.Search("A/S01-001",
                string.Empty, 20);
            Check("cardref finds a full card number",
                byCode.Count == 1 && byCode[0].Name == "火の勇者",
                byCode.Count.ToString());

            List<CardReferenceEntry> byName = index.Search("氷", string.Empty, 20);
            Check("cardref matches a name",
                byName.Count == 1 && byName[0].Id == "B/W02-010",
                byName.Count.ToString());

            List<CardReferenceEntry> byText = index.Search(string.Empty,
                "力量+1000", 20);
            Check("cardref matches the effect text",
                byText.Count == 1 && byText[0].Id == "A/S01-001",
                byText.Count.ToString());

            List<CardReferenceEntry> byOtherText = index.Search(string.Empty,
                "抽1张卡", 20);
            Check("cardref reads the other pack",
                byOtherText.Count == 1 && byOtherText[0].Id == "B/W02-010",
                byOtherText.Count.ToString());

            Check("cardref needs at least one box",
                index.Search(" ", " ", 20).Count == 0);
            Check("cardref reads no database as empty",
                CardReferenceIndex.Build((CardDatabase)null, null, null).Count == 0);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void TestCardEditorEncoding()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-cardedit-enc-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            SeriesFolder folder = CreateTestFolder(root);

            CardEditorDraft first = CreateTestDraft("TST/02-E001");
            first.Name = "中文卡片";
            CardEditorDraft second = CreateTestDraft("TST/02-E002");
            second.Name = "另一张";

            var utf16 = new UnicodeEncoding(false, true);
            string path = Path.Combine(root, "CardData.txt");
            string original =
                CardDataWriter.BuildBlock(first).Replace("\n", "\r\n") +
                "\r\n\r\n" +
                CardDataWriter.BuildBlock(second).Replace("\n", "\r\n") + "\r\n";
            File.WriteAllBytes(path,
                utf16.GetPreamble().Concat(utf16.GetBytes(original)).ToArray());

            List<WeissCard> parsed = ParseTestFolder(folder);
            Check("cardedit utf16 fixture parses", parsed.Count == 2,
                parsed.Count.ToString());

            CardEditorDraft edit = CardEditorDraft.FromCard(parsed[0], folder);
            edit.Level = 2;
            CardWriteResult saved = CardDataWriter.Save(edit);
            Check("cardedit utf16 save succeeds", saved.Success, saved.Message);

            byte[] bytes = File.ReadAllBytes(path);
            Check("cardedit utf16 keeps the BOM",
                bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xFE,
                bytes.Length.ToString());

            List<WeissCard> after = ParseTestFolder(folder);
            Check("cardedit utf16 keeps the other card",
                after.Count == 2 && after[1].Name == "另一张",
                after.Count == 2 ? after[1].Name : after.Count.ToString());
            Check("cardedit utf16 keeps CRLF",
                utf16.GetString(bytes, 2, bytes.Length - 2)
                    .Contains("Name 另一张\r\n"),
                "line ending changed");
            Check("cardedit utf16 writes the edited level",
                after.Count > 0 && after[0].Level == 2,
                after.Count > 0 ? after[0].Level.ToString() : "none");

            // A UTF-8 file without BOM must not grow one.
            CardEditorDraft plainDraft = CreateTestDraft("TST/02-E003");
            plainDraft.FileName = "Plain";
            plainDraft.FolderFullPath = root;
            File.WriteAllText(Path.Combine(root, "Plain.txt"),
                CardDataWriter.BuildBlock(plainDraft) + "\n",
                new UTF8Encoding(false));
            plainDraft.Name = "无 BOM";
            plainDraft.OriginalId = "TST/02-E003";
            CardWriteResult plainSaved = CardDataWriter.Save(plainDraft);
            Check("cardedit plain save succeeds", plainSaved.Success,
                plainSaved.Message);
            byte[] plainBytes = File.ReadAllBytes(Path.Combine(root, "Plain.txt"));
            Check("cardedit plain save keeps the file BOM free",
                plainBytes.Length > 0 && plainBytes[0] != 0xEF,
                plainBytes.Length.ToString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void TestCardEditorImage()
    {
        Check("cardedit image name normalizes the card number",
            CardDataWriter.NormalizeImageBaseName("AW/S18-E101") == "AW_S18_E101",
            CardDataWriter.NormalizeImageBaseName("AW/S18-E101"));
        Check("cardedit image name drops the extension",
            CardDataWriter.NormalizeImageBaseName("AW_S18_E101.png") ==
                "AW_S18_E101",
            CardDataWriter.NormalizeImageBaseName("AW_S18_E101.png"));

        string root = Path.Combine(Path.GetTempPath(),
            "ws-cardedit-img-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string source = Path.Combine(root, "drop.png");
            string folder = Path.Combine(root, "S01");
            File.WriteAllBytes(source, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            string copied = CardDataWriter.CopyImage(source, folder, "TST/03-E001");
            Check("cardedit image is copied",
                copied != null && File.Exists(copied) &&
                Path.GetFileName(copied) == "TST_03_E001.png",
                copied ?? "null");

            string notAnImage = Path.Combine(root, "bad.txt");
            File.WriteAllText(notAnImage, "not an image");
            Check("cardedit rejects a non image",
                CardDataWriter.CopyImage(notAnImage, folder, "TST/03-E002") == null);

            CardEditorDraft draft = CreateTestDraft("TST/03-E003");
            draft.IsNew = true;
            draft.FolderFullPath = folder;
            draft.FileName = "CardData";
            draft.ImageSourcePath = source;
            CardWriteResult saved = CardDataWriter.Save(draft);
            Check("cardedit save copies the dropped image",
                saved.Success && File.Exists(saved.ImagePath), saved.Message);

            string blockPath = Path.Combine(folder, "CardData.txt");
            string block = File.Exists(blockPath)
                ? File.ReadAllText(blockPath, Encoding.UTF8)
                : string.Empty;
            Check("cardedit save records the image id",
                block.Contains("Image TST_03_E003"), block);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// 添加异画 and ExtraArtworkLoader share <see cref="AltArtworkNaming"/>.
    /// These checks pin the rules (directory, file name, clone id) so a variant
    /// written today stays detectable tomorrow.
    /// </summary>
    private static void TestAltArtNaming()
    {
        Check("altart set code is the last prefix segment",
            AltArtworkNaming.GetSetCode("AW/S18") == "S18" &&
            AltArtworkNaming.GetSetCode("OS05/R05") == "R05" &&
            AltArtworkNaming.GetSetCode("PR") == "PR",
            AltArtworkNaming.GetSetCode("OS05/R05"));

        Check("altart file name joins number and variant letter",
            AltArtworkNaming.GetFileBaseName("AW/S18-E101", 'a') == "AW_S18_E101a" &&
            AltArtworkNaming.GetFileBasePrefix("AW/S18-E101") == "AW_S18_E101",
            AltArtworkNaming.GetFileBaseName("AW/S18-E101", 'a'));

        AltArtworkFileName parsed = AltArtworkNaming.Parse("AW_S18_E101a", "AW/S18");
        Check("altart parse reads the base card number",
            parsed != null && parsed.NormalCardId == "AW/S18-101" &&
            parsed.ParallelCardId == "AW/S18-E101" && parsed.Suffix == 'a',
            parsed == null
                ? "null"
                : parsed.NormalCardId + " / " + parsed.ParallelCardId);
        Check("altart parse keeps the clone id",
            parsed != null &&
            parsed.BuildVariantId("AW/S18-E101") == "AW/S18-E101a" &&
            parsed.MatchesBase("AW/S18-E101") &&
            !parsed.MatchesBase("AW/S18-102"),
            parsed == null ? "null" : parsed.BuildVariantId("AW/S18-E101"));

        AltArtworkFileName plain = AltArtworkNaming.Parse("os05_r05_001s", "OS05/R05");
        Check("altart parse folds the prefix and case",
            plain != null && plain.NormalCardId == "OS05/R05-001" &&
            plain.BuildVariantId("OS05/R05-001") == "OS05/R05-001s",
            plain == null ? "null" : plain.NormalCardId);

        AltArtworkFileName trial = AltArtworkNaming.Parse("KS_W49_TE16b", "KS/W49");
        Check("altart parse handles trial cards",
            trial != null && trial.NormalCardId == "KS/W49-T16" &&
            trial.ParallelCardId == "KS/W49-TE16" &&
            trial.BuildVariantId("KS/W49-T16") == "KS/W49-T16b",
            trial == null ? "null" : trial.NormalCardId + " / " + trial.ParallelCardId);

        Check("altart parse ignores a name without variant letter",
            AltArtworkNaming.Parse("AW_S18_E101", "AW/S18") == null &&
            AltArtworkNaming.Parse("AW_S18_E1011", "AW/S18") == null);
        Check("altart keeps letters as suffix only",
            AltArtworkNaming.IsUsableSuffix('a') &&
            AltArtworkNaming.IsUsableSuffix('s') &&
            !AltArtworkNaming.IsUsableSuffix('E') &&
            !AltArtworkNaming.IsUsableSuffix('1'));

        // A pack whose art lives in a directory named after the folder instead
        // of the set code (two shipped packs do) still resolves.
        string root = Path.Combine(Path.GetTempPath(),
            "ws-altart-dir-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "OS05-R05"));
            SeriesFolder folder = CreatePackFolder("Aokana/OS05-R05", "OS05/R05",
                "R05", "Aokana");
            Check("altart directory falls back to the folder name",
                string.Equals(Path.GetFileName(
                        AltArtworkNaming.ResolveDirectory(root, folder)),
                    "OS05-R05", StringComparison.OrdinalIgnoreCase),
                AltArtworkNaming.ResolveDirectory(root, folder));

            Directory.CreateDirectory(Path.Combine(root, "R05"));
            Check("altart directory prefers the set code",
                string.Equals(Path.GetFileName(
                        AltArtworkNaming.ResolveDirectory(root, folder)),
                    "R05", StringComparison.OrdinalIgnoreCase),
                AltArtworkNaming.ResolveDirectory(root, folder));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// 添加异画 writes a copy below AlternateArtwork and nothing else. These
    /// checks drive the writer and then let ExtraArtworkLoader read the file
    /// back, which is the only proof a variant is really detected.
    /// </summary>
    private static void TestAltArtWriter()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-altart-" + Guid.NewGuid().ToString("N"));
        try
        {
            string cardsRoot = Path.Combine(root, "Cards", "测试系列", "S01");
            string altRoot = Path.Combine(root, "AlternateArtwork");
            Directory.CreateDirectory(cardsRoot);
            Directory.CreateDirectory(altRoot);

            string cardData = Path.Combine(cardsRoot, "CardData.txt");
            File.WriteAllText(cardData,
                CardDataWriter.BuildBlock(CreateTestDraft("TST/01-001")),
                new UTF8Encoding(false));

            string image = Path.Combine(root, "art.png");
            File.WriteAllBytes(image, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            var request = new AltArtworkRequest();
            request.BaseCardId = "TST/01-001";
            request.Prefix = "TST/01";
            request.FolderPath = "测试系列/S01";
            request.FolderFullPath = cardsRoot;
            request.FileName = "CardData";
            request.AlternateArtworkRoot = altRoot;
            request.AltId = "TST/01-001a";
            request.ImageSourcePath = image;

            AltArtworkResult saved = AltArtworkWriter.Save(request);
            string expected = Path.Combine(altRoot, "01", "TST_01_001a.png");
            Check("altart writer copies the image into AlternateArtwork",
                saved.Success && File.Exists(expected) &&
                string.Equals(saved.FilePath, expected,
                    StringComparison.OrdinalIgnoreCase),
                saved.Message);

            // The loader has to build a clone from exactly that file.
            SeriesFolder folder = CreatePackFolder("测试系列/S01", "TST/01",
                "S01", "测试系列");
            folder.FullPath = cardsRoot;
            var warnings = new List<string>();
            folder.Cards.AddRange(CardDataParser.ParseFile(cardData, folder,
                CommonEffectLibrary.Empty, warnings));
            ExtraArtworkLoader.Load(folder, altRoot, warnings);

            WeissCard clone = null;
            for (int i = 0; i < folder.Cards.Count; i++)
            {
                if (folder.Cards[i].IsClone)
                {
                    clone = folder.Cards[i];
                    break;
                }
            }
            Check("altart loader finds the written variant",
                clone != null && clone.Id == "TST/01-001a" &&
                clone.CloneOf != null && clone.CloneOf.Id == "TST/01-001",
                clone == null ? "none" : clone.Id);
            Check("altart variant keeps the base card fields",
                clone != null && clone.Name == "测试卡" && clone.Power == 5000,
                clone == null ? "null" : clone.Name);

            var index = ImageLocator.BuildIndex(cardsRoot);
            if (clone != null)
                ImageLocator.Resolve(clone, index);
            Check("altart variant shows its own art",
                clone != null && !string.IsNullOrEmpty(clone.ImagePath) &&
                string.Equals(clone.ImagePath, expected,
                    StringComparison.OrdinalIgnoreCase),
                clone == null ? "null" : (clone.ImagePath ?? "null"));

            // Everything the loader would ignore has to be refused up front.
            Check("altart writer refuses a taken number",
                !AltArtworkWriter.Save(request).Success);

            string suggested = AltArtworkWriter.SuggestAltId("TST/01-001",
                "TST/01", "测试系列/S01", altRoot);
            Check("altart writer suggests the next free letter",
                suggested == "TST/01-001b", suggested);

            request.AltId = "TST/01-002a";
            Check("altart writer refuses another card number",
                !AltArtworkWriter.Save(request).Success);
            request.AltId = "TST/01-0011";
            Check("altart writer refuses a digit suffix",
                !AltArtworkWriter.Save(request).Success);
            request.AltId = "TST/01-001E";
            Check("altart writer refuses the E suffix",
                !AltArtworkWriter.Save(request).Success);
            request.AltId = "TST/01-001ab";
            Check("altart writer refuses two letters",
                !AltArtworkWriter.Save(request).Success);

            string webp = Path.Combine(root, "art.webp");
            File.WriteAllBytes(webp, new byte[] { 1, 2, 3 });
            request.AltId = "TST/01-001b";
            request.ImageSourcePath = webp;
            AltArtworkResult refused = AltArtworkWriter.Save(request);
            Check("altart writer refuses a format the game does not read",
                !refused.Success && refused.Message.Contains("png"),
                refused.Message);

            request.Prefix = string.Empty;
            request.ImageSourcePath = image;
            Check("altart writer refuses a pack without prefix",
                !AltArtworkWriter.Save(request).Success);

            // A CardData block that already owns the number would win over the
            // clone, so the writer refuses that number instead of writing art
            // the loader then drops.
            File.AppendAllText(cardData,
                "\r\nCharacter: TST/01-001c\r\nName 同名卡\r\nEndCard\r\n",
                new UTF8Encoding(false));
            request.Prefix = "TST/01";
            request.AltId = "TST/01-001c";
            AltArtworkResult blocked = AltArtworkWriter.Save(request);
            Check("altart writer refuses a number CardData already has",
                !blocked.Success && blocked.Message.Contains("已经有一张"),
                blocked.Message);

            // Cards whose CardData id carries the parallel E (AW/S18-E101) are
            // the common case; the written file has to resolve there too.
            string ePack = Path.Combine(root, "Cards", "测试系列", "S02");
            Directory.CreateDirectory(ePack);
            string eCardData = Path.Combine(ePack, "CardData.txt");
            File.WriteAllText(eCardData,
                CardDataWriter.BuildBlock(CreateTestDraft("TST/02-E001")),
                new UTF8Encoding(false));

            var eRequest = new AltArtworkRequest();
            eRequest.BaseCardId = "TST/02-E001";
            eRequest.Prefix = "TST/02";
            eRequest.FolderPath = "测试系列/S02";
            eRequest.FolderFullPath = ePack;
            eRequest.FileName = "CardData";
            eRequest.AlternateArtworkRoot = altRoot;
            eRequest.AltId = "TST/02-E001b";
            eRequest.ImageSourcePath = image;
            AltArtworkResult eSaved = AltArtworkWriter.Save(eRequest);
            Check("altart writer writes the E form of a card number",
                eSaved.Success && File.Exists(Path.Combine(altRoot, "02",
                    "TST_02_E001b.png")), eSaved.Message);

            SeriesFolder eFolder = CreatePackFolder("测试系列/S02", "TST/02",
                "S02", "测试系列");
            eFolder.FullPath = ePack;
            eFolder.Cards.AddRange(CardDataParser.ParseFile(eCardData, eFolder,
                CommonEffectLibrary.Empty, warnings));
            ExtraArtworkLoader.Load(eFolder, altRoot, warnings);

            WeissCard eClone = null;
            for (int i = 0; i < eFolder.Cards.Count; i++)
            {
                if (eFolder.Cards[i].IsClone)
                {
                    eClone = eFolder.Cards[i];
                    break;
                }
            }
            Check("altart loader keeps the E form clone id",
                eClone != null && eClone.Id == "TST/02-E001b" &&
                eClone.CloneOf != null && eClone.CloneOf.Id == "TST/02-E001",
                eClone == null ? "none" : eClone.Id);

            // 保存 N 张: the offered letter skips what the folder shows and what
            // the queue already handed out, and a refused row does not stop the
            // rows after it.
            string queued = AltArtworkWriter.SuggestAltId("TST/01-001",
                "TST/01", "测试系列/S01", altRoot,
                new HashSet<char> { 'a' });
            Check("altart writer skips letters already handed out",
                queued == "TST/01-001b", queued);

            var batch = new List<AltArtworkRequest>();
            batch.Add(CreateAltArtRequest("TST/01-001b", image, cardsRoot, altRoot));
            batch.Add(CreateAltArtRequest("TST/01-001c", image, cardsRoot, altRoot));
            AltArtBatchResult batchResult = AltArtworkWriter.SaveAll(batch);
            Check("altart writer keeps the rest of a queue",
                batchResult.Results.Count == 2 &&
                batchResult.SavedCount == 1 && batchResult.FailedCount == 1 &&
                batchResult.Results[0].Success &&
                !batchResult.Results[1].Success &&
                File.Exists(Path.Combine(altRoot, "01", "TST_01_001b.png")),
                batchResult.Message);
            Check("altart batch message names both halves",
                batchResult.Message.Contains("已保存 1 张") &&
                batchResult.Message.Contains("1 张失败"),
                batchResult.Message);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// 添加卡片 picks the card pack from the 卡号 alone: the head of the number
    /// is the `Prefix` line of SingleSetData.txt. Synthetic metadata keeps the
    /// ranking rules readable and independent of the game folder.
    /// </summary>
    private static void TestCardPackIndex()
    {
        Check("packindex head of a normal card number",
            CardPackIndex.CardIdHead("AW/S18-E101") == "AW/S18",
            CardPackIndex.CardIdHead("AW/S18-E101"));
        Check("packindex head ignores the rarity suffix",
            CardPackIndex.CardIdHead("MTI/S83-E055S") == "MTI/S83",
            CardPackIndex.CardIdHead("MTI/S83-E055S"));
        Check("packindex head folds case, underscores and spaces",
            CardPackIndex.CardIdHead(" hol_w104_041 ") == "HOL/W104",
            CardPackIndex.CardIdHead(" hol_w104_041 "));
        Check("packindex head keeps a prefix without a set",
            CardPackIndex.CardIdHead("AW") == "AW",
            CardPackIndex.CardIdHead("AW"));
        Check("packindex head of an empty number",
            CardPackIndex.CardIdHead(null) == string.Empty &&
            CardPackIndex.CardIdHead("   ") == string.Empty);

        var accel = new SeriesDefinition("加速世界");
        SeriesFolder awS18 = CreatePackFolder("Accel World/S18", "AW/S18",
            "加速世界 (S18)", "加速世界");
        SeriesFolder awPr = CreatePackFolder("Accel World/PR", "AW/S18",
            "加速世界 PR", "加速世界");
        accel.Folders.Add(awS18);
        accel.Folders.Add(awPr);

        var idol = new SeriesDefinition("偶像大师");
        SeriesFolder iasS93 = CreatePackFolder("Idolmaster/S93/IAS", "IAS/S93",
            "IAS (S93)", "偶像大师");
        SeriesFolder iasSe52 = CreatePackFolder("Idolmaster/SE52/IAS", "IAS/S93",
            "IAS (SE52)", "偶像大师");
        idol.Folders.Add(iasS93);
        idol.Folders.Add(iasSe52);

        var daCapo = new SeriesDefinition("初音岛");
        SeriesFolder dsW46 = CreatePackFolder("DaCapo/DS_W46", "DS/W46",
            "D.C. (W46)", "初音岛");
        SeriesFolder dsPr = CreatePackFolder("DaCapo/DS_PR", "DS/W46",
            "D.C. PR", "初音岛");
        daCapo.Folders.Add(dsW46);
        daCapo.Folders.Add(dsPr);

        var kusuriya = new SeriesDefinition("药屋少女");
        SeriesFolder ksr01 = CreatePackFolder("Apothecary Diaries/KSR01", "KSR/01B",
            "药屋少女 (01)", "药屋少女");
        SeriesFolder ksr02 = CreatePackFolder("Kusuriya/01", "KSR/01B",
            "药屋少女 01", "药屋少女");
        kusuriya.Folders.Add(ksr01);
        kusuriya.Folders.Add(ksr02);

        // A card table whose abbreviation differs from the declared prefix:
        // the set code still points at a single pack.
        var accelS19 = new SeriesDefinition("加速世界");
        SeriesFolder awS19 = CreatePackFolder("Accel World/S19", "GAW/S19",
            "加速世界 (S19)", "加速世界");
        accelS19.Folders.Add(awS19);

        // The same folder declared by two series must not count twice.
        var duplicate = new SeriesDefinition("加速世界");
        duplicate.Folders.Add(awS18);

        var series = new List<SeriesDefinition>
        {
            accel, idol, daCapo, kusuriya, accelS19, duplicate
        };
        CardPackIndex index = CardPackIndex.Build(series,
            delegate(SeriesFolder folder) { return "C:\\Cards\\" + folder.FolderPath; });

        Check("packindex counts folders, not prefixes", index.Count == 9,
            index.Count.ToString());
        Check("packindex prefers the folder named after the set",
            SamePackage(index.Resolve("AW/S18-E101"), "Accel World/S18"),
            DescribePackages(index.Find("AW/S18-E101")));
        Check("packindex keeps a shared prefix visible",
            index.Find("AW/S18-E101").Count == 2,
            DescribePackages(index.Find("AW/S18-E101")));
        Check("packindex prefers a path segment carrying the set",
            SamePackage(index.Resolve("IAS/S93-001"), "Idolmaster/S93/IAS"),
            DescribePackages(index.Find("IAS/S93-001")));
        Check("packindex prefers the only folder mentioning the set",
            SamePackage(index.Resolve("DS/W46-001"), "DaCapo/DS_W46"),
            DescribePackages(index.Find("DS/W46-001")));
        Check("packindex does not guess between equal candidates",
            index.Resolve("KSR/01B-001") == null &&
            index.Find("KSR/01B-001").Count == 2,
            DescribePackages(index.Find("KSR/01B-001")));
        Check("packindex falls back to a unique set code",
            SamePackage(index.Resolve("AW/S19-E001"), "Accel World/S19"),
            DescribePackages(index.Find("AW/S19-E001")));
        Check("packindex ignores an unknown prefix",
            index.Resolve("ZZZ/99-001") == null &&
            index.Find("ZZZ/99-001").Count == 0,
            DescribePackages(index.Find("ZZZ/99-001")));

        CardPackTarget writeable = index.Resolve("AW/S18-E101");
        Check("packindex hands out the folder the writer needs",
            writeable != null &&
            writeable.Folder.FullPath == "C:\\Cards\\Accel World/S18",
            writeable == null ? "missing" : writeable.Folder.FullPath);

        CardPackIndex empty = CardPackIndex.Build(null, null);
        Check("packindex of a call without metadata is empty",
            empty.Count == 0 && empty.Find("AW/S18-E101").Count == 0);
    }

    /// <summary>Card pack folder of a synthetic series, as SingleSetData writes it.</summary>
    private static SeriesFolder CreatePackFolder(string relativePath, string prefix,
        string setName, string seriesName)
    {
        var folder = new SeriesFolder();
        folder.SeriesName = seriesName;
        folder.FolderPath = relativePath;
        folder.Prefix = prefix;
        folder.Prefixes.Add(prefix);
        folder.SetName = setName;
        folder.FileName = "CardData";
        return folder;
    }

    private static bool SamePackage(CardPackTarget target, string relativePath)
    {
        return target != null && target.Folder != null &&
            string.Equals(target.Folder.FolderPath, relativePath,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribePackages(IList<CardPackTarget> targets)
    {
        if (targets == null)
            return "null";
        if (targets.Count == 0)
            return "none";

        var parts = new List<string>();
        for (int i = 0; i < targets.Count; i++)
        {
            parts.Add(targets[i].Folder == null
                ? "?"
                : targets[i].Folder.FolderPath);
        }
        return string.Join(" | ", parts.ToArray());
    }

    /// <summary>One 添加异画 request of the batch tests.</summary>
    private static AltArtworkRequest CreateAltArtRequest(string altId,
        string image, string cardsRoot, string altRoot)
    {
        var request = new AltArtworkRequest();
        request.BaseCardId = "TST/01-001";
        request.Prefix = "TST/01";
        request.FolderPath = "测试系列/S01";
        request.FolderFullPath = cardsRoot;
        request.FileName = "CardData";
        request.AlternateArtworkRoot = altRoot;
        request.AltId = altId;
        request.ImageSourcePath = image;
        return request;
    }

    /// <summary>
    /// 添加异画 in the editor page: the right column grows the block for an
    /// existing card, one dropped image becomes one queued row with its own
    /// letter, the queue survives a save that only wrote part of it, and the
    /// block refuses to save before an image was picked.
    /// </summary>
    private static void TestAltArtPage()
    {
        Exception error = null;
        var worker = new Thread(delegate()
        {
            try
            {
                TestAltArtPageCore();
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
            Fail("altart editor page runs without a screen", error.ToString());
    }

    private static void TestAltArtPageCore()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-altart-page-" + Guid.NewGuid().ToString("N"));
        string altRoot = Path.Combine(root, "AlternateArtwork");
        string image = Path.Combine(root, "alt.png");
        string second = Path.Combine(root, "alt2.png");
        try
        {
            Directory.CreateDirectory(Path.Combine(altRoot, "01"));
            Directory.CreateDirectory(Path.Combine(root, "S01"));
            File.WriteAllBytes(image, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            File.WriteAllBytes(second, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            var series = new SeriesDefinition("测试系列");
            SeriesFolder folder = CreatePackFolder("测试系列/S01", "TST/01",
                "S01", "测试系列");
            folder.FullPath = Path.Combine(root, "S01");
            series.Folders.Add(folder);

            var draft = new CardEditorDraft();
            draft.IsNew = false;
            draft.Id = "TST/01-001";
            draft.OriginalId = "TST/01-001";
            draft.Name = "测试卡";
            draft.Prefix = "TST/01";
            draft.FolderPath = folder.FolderPath;
            draft.FolderFullPath = folder.FullPath;

            using (var page = new CardEditorPage())
            {
                // A page that was never shown reports every child as invisible.
                page.Visible = true;
                page.AlternateArtworkRoot = altRoot;
                page.AltArtBaseCardId = "TST/01-001";
                page.LoadDraft(draft, series.Folders);

                Control section = FindEditorControl(page, "_altArtSection");
                Check("altart page shows the block for an existing card",
                    section != null && section.Visible);

                FieldBox altId = FindEditorField(page, "_altIdBox");
                Check("altart page suggests the first free letter",
                    altId != null && altId.Input.Text == "TST/01-001a",
                    altId == null ? "null" : altId.Input.Text);

                page.Size = new Size(1400, 900);
                page.CreateControl();
                LayOutDeep(page);
                Control altImage = FindEditorControl(page, "_altImageBox");
                Control strip = FindEditorControl(page, "_altQueueStrip");
                var column = section == null ? null : section.Parent as TableLayoutPanel;
                Check("altart page keeps the block height",
                    column != null && column.RowStyles[0].Height == 168F,
                    column == null ? "null" : column.RowStyles[0].Height.ToString());
                Check("altart page gives the preview the whole box",
                    altImage != null && altImage.Width >= 86 &&
                    altImage.Height >= 100 &&
                    altId != null && altId.Width >= 150 && altId.Height >= 26,
                    (altImage == null
                        ? "null"
                        : altImage.Width + "x" + altImage.Height) +
                    " / " + (altId == null
                        ? "null"
                        : altId.Width + "x" + altId.Height));
                Check("altart page lays out the queue strip",
                    strip != null && strip.Width >= 300 && strip.Height >= 72,
                    strip == null ? "null" : strip.Width + "x" + strip.Height);

                // The strip derives its tiles from its own height, so the
                // mirrored numbers prove the band grew from ~49x35 tiles to
                // ~79x56 ones.
                PropertyInfo tileWidthProperty = strip == null
                    ? null
                    : strip.GetType().GetProperty("TileWidth",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                PropertyInfo tileHeightProperty = strip == null
                    ? null
                    : strip.GetType().GetProperty("TileHeight",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                int tileWidth = tileWidthProperty == null
                    ? 0
                    : (int)tileWidthProperty.GetValue(strip, null);
                int tileHeight = tileHeightProperty == null
                    ? 0
                    : (int)tileHeightProperty.GetValue(strip, null);
                Check("altart page grows the queue thumbnails",
                    tileWidth >= 56 && tileHeight >= 79,
                    tileWidth + "x" + tileHeight);

                var altHost = section as TableLayoutPanel;
                Control altSave = FindEditorControl(page, "_altSaveButton");
                Control altClear = FindEditorControl(page, "_altClearButton");
                var altActions = altSave == null
                    ? null
                    : altSave.Parent as FlowLayoutPanel;
                Check("altart page grows the preview below the caption",
                    altHost != null && altImage != null &&
                    altHost.RowCount == 2 &&
                    altHost.GetRow(altImage) == 1 &&
                    altHost.GetRowSpan(altImage) == 1 &&
                    altImage.Width >= 100 && altImage.Height >= 128,
                    altImage == null
                        ? "null"
                        : altImage.Width + "x" + altImage.Height);
                Check("altart page stacks 保存 over 清空 right of the queue",
                    altHost != null && altActions != null && altSave != null &&
                    altClear != null && strip != null &&
                    altActions.FlowDirection == FlowDirection.TopDown &&
                    altActions.Controls.Count == 2 &&
                    altActions.Controls[0] == altSave &&
                    altActions.Controls[1] == altClear &&
                    altSave.Text == "保存" && altClear.Text == "清空" &&
                    altActions.Left >= strip.Right,
                    altActions == null
                        ? "null"
                        : altActions.FlowDirection + " / " +
                          altActions.Controls.Count + " controls at " +
                          altActions.Left);
                Check("altart page pins the stacked pair to the bottom right",
                    altActions != null && altSave != null && altClear != null &&
                    altActions.ClientSize.Width > 0 &&
                    altActions.ClientSize.Height > 0 &&
                    altClear.Right == altActions.ClientSize.Width &&
                    altClear.Bottom == altActions.ClientSize.Height &&
                    altSave.Bottom <= altClear.Top,
                    altActions == null || altClear == null || altSave == null
                        ? "null"
                        : altSave.Left + "-" + altSave.Right + "," +
                          altSave.Top + "-" + altSave.Bottom + " / " +
                          altClear.Left + "-" + altClear.Right + "," +
                          altClear.Top + "-" + altClear.Bottom + " of " +
                          altActions.ClientSize.Width + "x" +
                          altActions.ClientSize.Height);

                Check("altart page wants the art first",
                    page.ReadAltArts() == null && StatusText(page).Contains("异画卡图"),
                    StatusText(page));

                MethodInfo append = typeof(CardEditorPage).GetMethod(
                    "AppendAltArtImages",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (append != null)
                    append.Invoke(page, new object[] { new string[] { image, second } });

                IList<AltArtworkRequest> requests = page.ReadAltArts();
                Check("altart page builds one request per image",
                    requests != null && requests.Count == 2 &&
                    requests[0].AltId == "TST/01-001a" &&
                    requests[1].AltId == "TST/01-001b" &&
                    requests[0].BaseCardId == "TST/01-001" &&
                    requests[0].Prefix == "TST/01" &&
                    requests[0].FolderPath == "测试系列/S01" &&
                    requests[0].FolderFullPath == folder.FullPath &&
                    requests[0].FileName == "CardData" &&
                    requests[0].ImageSourcePath == image &&
                    requests[1].ImageSourcePath == second,
                    requests == null
                        ? "null"
                        : requests.Count + " / " + requests[0].AltId +
                            " / " + requests[1].AltId);

                // Only the first image made it to disk: it leaves the queue, the
                // refused one stays for a retry.
                page.NotifyAltArtsSaved(AltArtworkWriter.SaveAll(
                    new List<AltArtworkRequest> { requests[0] }));
                AltArtQueueStrip queue = strip as AltArtQueueStrip;
                Check("altart page drops the saved image from the queue",
                    queue != null && queue.Count == 1 &&
                    queue.Items[0].SourcePath == second &&
                    queue.SelectedIndex == 0 &&
                    altId.Input.Text == "TST/01-001b",
                    queue == null
                        ? "null"
                        : queue.Count + " / " + altId.Input.Text);

                // The ✕ of a tile goes through RemoveAltArtItem.
                MethodInfo remove = typeof(CardEditorPage).GetMethod(
                    "RemoveAltArtItem",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (remove != null)
                    remove.Invoke(page, new object[] { 0 });
                Check("altart page can drop a queued image",
                    queue != null && queue.Count == 0,
                    queue == null ? "null" : queue.Count.ToString());

                // 添加卡片 has no base card in CardData, so the block folds away.
                page.LoadDraft(CardEditorDraft.CreateBlank(series, folder),
                    series.Folders);
                Check("altart page folds the block for 添加卡片",
                    section != null && !section.Visible);
                Check("altart page refuses a new card",
                    page.ReadAltArts() == null, StatusText(page));

                page.UnloadDraft();
                Check("altart page clears the queue on unload",
                    queue != null && queue.Count == 0);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Image drops: the 卡图 / 异画 boxes own their file drops, so the window
    /// wide 卡表压缩包 import must not answer them, and a drop carries every
    /// supported image (a folder contributes the png / jpg files inside it).
    /// </summary>
    private static void TestCardImageDrops()
    {
        Exception error = null;
        var worker = new Thread(delegate()
        {
            try
            {
                TestCardImageDropsCore();
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
            Fail("image drop boxes run without a screen", error.ToString());
    }

    private static void TestCardImageDropsCore()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ws-drop-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string png = Path.Combine(root, "a.png");
            string jpg = Path.Combine(root, "b.JPG");
            string txt = Path.Combine(root, "c.txt");
            File.WriteAllBytes(png, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            File.WriteAllBytes(jpg, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
            File.WriteAllText(txt, "x");

            using (var box = new CardImageDropBox())
            {
                Check("drop scope leaves image boxes alone",
                    !CardPackDropScope.Accepts(box));
            }

            using (var panel = new Panel())
            {
                Check("drop scope covers the rest of the window",
                    CardPackDropScope.Accepts(panel));
            }
            Check("drop scope accepts no control at all",
                !CardPackDropScope.Accepts(null));

            var mixed = new DataObject(DataFormats.FileDrop,
                new string[] { txt, png, root });
            List<string> paths = CardImageDropBox.GetImagePaths(mixed);
            Check("image drop keeps every supported file",
                paths.Count == 2 && paths.Contains(png) && paths.Contains(jpg),
                string.Join(", ", paths.ToArray()));

            var ordered = new DataObject(DataFormats.FileDrop,
                new string[] { png, jpg });
            List<string> only = CardImageDropBox.GetImagePaths(ordered);
            Check("image drop keeps the order Explorer sent",
                only.Count == 2 && only[0] == png && only[1] == jpg,
                string.Join(", ", only.ToArray()));

            var args = new CardImageDroppedEventArgs(only);
            Check("image drop event keeps every path",
                args.Paths.Count == 2 && args.Path == png);

            var single = new CardImageDroppedEventArgs(png);
            Check("image drop event still carries one path",
                single.Paths.Count == 1 && single.Path == png);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// 启动游戏 entry: the exe is found by walking up from the card data folder
    /// and started with the same `noUpdate` argument as 不热更启动游戏.lnk, so the
    /// bundled updater never rewrites the card table this app edits. Nothing
    /// here starts a process, so the group is safe to run unattended.
    /// </summary>
    private static void TestGameLauncher(Func<CardDatabase> db)
    {
        string dataRoot = db().DataRoot;
        string gameRoot = GameLauncher.FindGameRoot(dataRoot);
        Check("launcher finds the game root",
            !string.IsNullOrEmpty(gameRoot) &&
            File.Exists(Path.Combine(gameRoot, GameLauncher.ExeName)),
            gameRoot ?? "null");

        string error;
        GameLaunchTarget target = GameLauncher.Resolve(dataRoot, out error);
        Check("launcher resolves the exe",
            target != null && File.Exists(target.ExePath),
            error ?? "no target");
        Check("launcher passes noUpdate",
            target != null && target.Arguments == GameLauncher.NoUpdateArgument,
            target == null ? "no target" : target.Arguments);
        Check("launcher runs from the game root",
            target != null &&
            string.Equals(Path.GetDirectoryName(target.ExePath), target.GameRoot,
                StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(target.GameRoot),
            target == null ? "no target" : target.GameRoot);
        Check("launcher refuses a missing target",
            !GameLauncher.TryLaunch(null, out error) &&
            !string.IsNullOrEmpty(error),
            error ?? "no error");

        // A fake install laid out like the real one, plus a sibling folder with
        // no exe at all: the first must resolve, the second must explain.
        string root = Path.Combine(Path.GetTempPath(),
            "ws-launcher-" + Guid.NewGuid().ToString("N"));
        try
        {
            string install = Path.Combine(root, "install");
            string nested = Path.Combine(install, "Weiss Schwarz_Data",
                "StreamingAssets", "Cards");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(install, GameLauncher.ExeName),
                string.Empty);

            string climbed = GameLauncher.FindGameRoot(nested);
            Check("launcher climbs to the install root",
                string.Equals(climbed, install, StringComparison.OrdinalIgnoreCase),
                climbed ?? "null");

            GameLaunchTarget nestedTarget =
                GameLauncher.Resolve(nested, out error);
            Check("launcher resolves a nested data folder",
                nestedTarget != null &&
                string.Equals(nestedTarget.GameRoot, install,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(nestedTarget.ExePath,
                    Path.Combine(install, GameLauncher.ExeName),
                    StringComparison.OrdinalIgnoreCase),
                nestedTarget == null ? (error ?? "no target") :
                    nestedTarget.ExePath);

            string noGame = Path.Combine(root, "no-game");
            Directory.CreateDirectory(noGame);
            GameLaunchTarget missing = GameLauncher.Resolve(noGame, out error);
            Check("launcher explains a missing exe",
                missing == null && !string.IsNullOrEmpty(error) &&
                error.Contains(GameLauncher.ExeName) && error.Contains("找不到"),
                error ?? "no error");

            // WS_GAME_EXE points the entry at an install outside the tree.
            string previous =
                Environment.GetEnvironmentVariable(GameLauncher.ExePathVariable);
            try
            {
                Environment.SetEnvironmentVariable(GameLauncher.ExePathVariable,
                    Path.Combine(install, GameLauncher.ExeName));
                string overridden = GameLauncher.FindGameRoot(noGame);
                Check("launcher honours WS_GAME_EXE",
                    string.Equals(overridden, install,
                        StringComparison.OrdinalIgnoreCase),
                    overridden ?? "null");
            }
            finally
            {
                Environment.SetEnvironmentVariable(GameLauncher.ExePathVariable,
                    previous);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
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
