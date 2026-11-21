using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Terminals;
using Xunit;

namespace OpenCw.Ui.Tests;

public class HackingBoardTests
{
    static readonly string[] Pool = { "COLD", "CORD", "HOLD", "COOL", "FORD", "WORD", "BOLD", "GOLD", "FOLD", "MOLD" };

    static HackingBoard Board(int seed = 1, int words = 6, int guesses = 4, string[]? pool = null) => new(pool ?? Pool, words, guesses, seed);

    static List<HackingHit> Hits(HackingBoard board, HackingHitKind kind) =>
        Enumerable.Range(0, board.Geometry.Cells).Select(board.HitAt).Where(h => h?.Kind == kind).Select(h => h!.Value).Distinct().ToList();

    static HackingHit PasswordHit(HackingBoard board) =>
        Hits(board, HackingHitKind.Word).Single(h => Text(board, h) == board.Password);

    static string Text(HackingBoard board, HackingHit hit) => new(Enumerable.Range(hit.Start, hit.Length).Select(board.CellAt).ToArray());

    [Theory]
    [InlineData(0, 0, 4)]
    [InlineData(0, 1, 5)]
    [InlineData(1, 0, 6)]
    [InlineData(3, 1, 11)]
    [InlineData(4, 1, 12)]
    public void Word_length_follows_level_and_the_reference_id_bit_capped_at_twelve(int level, int bit, int expected) =>
        Assert.Equal(expected, HackingRules.WordLength(level, bit));

    [Theory]
    [InlineData(1f, 20)]
    [InlineData(5f, 14)]
    [InlineData(10f, 6)]
    [InlineData(0f, 20)]
    [InlineData(99f, 6)]
    public void Word_count_falls_as_the_stat_rises_between_the_gamesetting_bounds(float stat, int expected) =>
        Assert.Equal(expected, HackingRules.WordCount(stat));

    [Fact]
    public void Word_count_respects_a_lower_configured_maximum_and_clamps_the_minimum()
    {
        Assert.Equal(10, HackingRules.WordCount(1f, 6, 10));
        Assert.Equal(8, HackingRules.WordCount(1f, 12, 8));
    }

    [Theory]
    [InlineData(4f, 4)]
    [InlineData(0f, 1)]
    [InlineData(9.9f, 5)]
    [InlineData(2.9f, 2)]
    public void Guesses_truncate_and_clamp_to_one_through_five(float adjusted, int expected) =>
        Assert.Equal(expected, HackingRules.Guesses(adjusted));

    [Fact]
    public void The_board_has_the_standard_geometry_with_only_words_and_junk()
    {
        var board = Board();
        Assert.Equal(HackingGeometry.Standard, board.Geometry);
        var words = Hits(board, HackingHitKind.Word);
        Assert.Equal(6, words.Count);
        Assert.All(words, w => Assert.Equal(4, w.Length));
        Assert.Contains(board.Password, words.Select(w => Text(board, w)));
        var wordCells = words.SelectMany(w => Enumerable.Range(w.Start, w.Length)).ToHashSet();
        for (int i = 0; i < board.Geometry.Cells; i++)
            Assert.True(wordCells.Contains(i) ? char.IsAsciiLetterUpper(board.CellAt(i)) : HackingBoard.Junk.Contains(board.CellAt(i)), $"cell {i}");
        Assert.Equal(30, HackingBoard.Junk.Length);
    }

    [Fact]
    public void No_word_crosses_the_column_boundary_or_overlaps_another()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var words = Hits(Board(seed, 20, pool: Enumerable.Range(0, 30).Select(i => $"W{(char)('A' + i % 26)}{(char)('A' + i / 26)}{(char)('A' + (i * 7) % 26)}").ToArray()), HackingHitKind.Word)
                .OrderBy(h => h.Start).ToList();
            Assert.Equal(20, words.Count);
            Assert.All(words, w => Assert.Equal(w.Start / 192, (w.Start + w.Length - 1) / 192));
            for (int i = 1; i < words.Count; i++)
                Assert.True(words[i - 1].Start + words[i - 1].Length <= words[i].Start, $"seed {seed}");
        }
    }

    [Theory]
    [InlineData(2, 4)]
    [InlineData(6, 4)]
    [InlineData(20, 4)]
    [InlineData(20, 12)]
    [InlineData(32, 6)]
    [InlineData(99, 6)]
    public void Every_word_sits_inside_one_rendered_row_and_no_row_holds_two_words(int count, int length)
    {
        string[] pool = Enumerable.Range(0, 40).Select(i => new string('W', length - 2) + (char)('A' + i / 26) + (char)('A' + i % 26)).ToArray();
        int perRow = HackingGeometry.Standard.CellsPerRow;
        for (int seed = 0; seed < 100; seed++)
        {
            var words = Hits(Board(seed, count, pool: pool), HackingHitKind.Word);
            Assert.Equal(Math.Min(count, 32), words.Count);
            Assert.All(words, w => Assert.Equal(w.Start / perRow, (w.Start + w.Length - 1) / perRow));
            Assert.Equal(words.Count, words.Select(w => w.Start / perRow).Distinct().Count());
        }
    }

    [Fact]
    public void The_same_seed_builds_the_same_board_and_another_seed_differs()
    {
        string Dump(HackingBoard b) => new(Enumerable.Range(0, b.Geometry.Cells).Select(b.CellAt).ToArray());
        Assert.Equal(Dump(Board(7)), Dump(Board(7)));
        Assert.NotEqual(Dump(Board(7)), Dump(Board(8)));
    }

    [Fact]
    public void A_wrong_word_costs_a_guess_and_reports_same_position_likeness()
    {
        var board = Board();
        var wrong = Hits(board, HackingHitKind.Word).First(h => Text(board, h) != board.Password);
        string guess = Text(board, wrong);
        int expected = guess.Zip(board.Password, (a, b) => a == b ? 1 : 0).Sum();
        var result = board.Select(wrong);
        Assert.Equal(HackingGuessKind.Wrong, result.Kind);
        Assert.Equal(expected, result.Likeness);
        Assert.Equal(3, board.AttemptsRemaining);
    }

    [Fact]
    public void Guessing_the_same_wrong_word_again_is_ignored_and_free()
    {
        var board = Board();
        var wrong = Hits(board, HackingHitKind.Word).First(h => Text(board, h) != board.Password);
        board.Select(wrong);
        Assert.Equal(HackingGuessKind.Ignored, board.Select(wrong).Kind);
        Assert.Equal(3, board.AttemptsRemaining);
    }

    [Fact]
    public void The_password_is_correct_and_ends_the_board()
    {
        var board = Board();
        Assert.Equal(HackingGuessKind.Correct, board.Select(PasswordHit(board)).Kind);
        Assert.Equal(4, board.AttemptsRemaining);
        var wrong = Hits(board, HackingHitKind.Word).First(h => Text(board, h) != board.Password);
        Assert.Equal(HackingGuessKind.Ignored, board.Select(wrong).Kind);
    }

    [Fact]
    public void The_last_wrong_guess_locks_out_and_the_board_then_ignores_input()
    {
        var board = Board(guesses: 2);
        var wrongs = Hits(board, HackingHitKind.Word).Where(h => Text(board, h) != board.Password).Take(3).ToList();
        Assert.Equal(HackingGuessKind.Wrong, board.Select(wrongs[0]).Kind);
        Assert.Equal(HackingGuessKind.LockedOut, board.Select(wrongs[1]).Kind);
        Assert.Equal(0, board.AttemptsRemaining);
        Assert.Equal(HackingGuessKind.Ignored, board.Select(PasswordHit(board)).Kind);
    }

    [Fact]
    public void A_stale_or_invented_hit_is_ignored()
    {
        var board = Board();
        int notAStart = Enumerable.Range(0, board.Geometry.Cells).First(i => Hits(board, HackingHitKind.Word).All(w => w.Start != i));
        Assert.Equal(HackingGuessKind.Ignored, board.Select(new HackingHit(notAStart, 4, HackingHitKind.Word)).Kind);
        Assert.Equal(HackingGuessKind.Ignored, board.Select(new HackingHit(383, 2, HackingHitKind.Bracket)).Kind);
        Assert.Null(board.HitAt(-1));
        Assert.Null(board.HitAt(board.Geometry.Cells));
        Assert.Equal(4, board.AttemptsRemaining);
    }

    [Fact]
    public void Brackets_stay_inside_one_row_and_hold_only_junk_between_the_pair()
    {
        int seen = 0;
        for (int seed = 0; seed < 40; seed++)
        {
            var board = Board(seed);
            foreach (var hit in Hits(board, HackingHitKind.Bracket))
            {
                seen++;
                Assert.Equal(hit.Start / 12, (hit.Start + hit.Length - 1) / 12);
                Assert.Contains(board.CellAt(hit.Start), "([{<");
                Assert.Equal(")]}>"["([{<".IndexOf(board.CellAt(hit.Start))], board.CellAt(hit.Start + hit.Length - 1));
                for (int i = hit.Start + 1; i < hit.Start + hit.Length - 1; i++)
                    Assert.Contains(board.CellAt(i), HackingBoard.Junk);
            }
        }
        Assert.True(seen > 20, "random boards should produce brackets");
    }

    [Fact]
    public void A_bracket_removes_a_dud_or_restores_attempts_once_and_is_then_used_up()
    {
        int duds = 0, restores = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            var board = Board(seed);
            var wrong = Hits(board, HackingHitKind.Word).First(h => Text(board, h) != board.Password);
            board.Select(wrong);
            int boardRestores = 0;
            foreach (var bracket in Hits(board, HackingHitKind.Bracket))
            {
                if (board.HitAt(bracket.Start) is not { Kind: HackingHitKind.Bracket }) continue;
                var result = board.Select(bracket);
                Assert.Null(board.HitAt(bracket.Start)?.Kind == HackingHitKind.Bracket ? board.HitAt(bracket.Start) : null);
                Assert.Equal(HackingGuessKind.Ignored, board.Select(bracket).Kind);
                if (result.Kind == HackingGuessKind.AttemptsRestored)
                {
                    boardRestores++;
                    restores++;
                    Assert.Equal(board.MaxAttempts, board.AttemptsRemaining);
                }
                else
                {
                    Assert.Equal(HackingGuessKind.DudRemoved, result.Kind);
                    duds++;
                }
            }
            Assert.True(boardRestores <= 1, $"seed {seed}");
        }
        Assert.True(duds > 0 && restores > 0);
    }

    [Fact]
    public void Dud_removal_blanks_a_non_password_word_that_can_no_longer_be_picked()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var board = Board(seed);
            if (Hits(board, HackingHitKind.Bracket).Count == 0) continue;
            var before = Hits(board, HackingHitKind.Word);
            HackingGuess result = default;
            foreach (var bracket in Hits(board, HackingHitKind.Bracket))
            {
                result = board.Select(bracket);
                if (result.Kind == HackingGuessKind.DudRemoved) break;
            }
            if (result.Kind != HackingGuessKind.DudRemoved) continue;
            var after = Hits(board, HackingHitKind.Word);
            var gone = before.Except(after).Single();
            Assert.All(Enumerable.Range(gone.Start, gone.Length), i => Assert.Equal(HackingBoard.Removed, board.CellAt(i)));
            Assert.Null(board.HitAt(gone.Start));
            Assert.Contains(board.Password, after.Select(h => Text(board, h)));
            return;
        }
        Assert.Fail("no seed produced a dud removal");
    }

    [Fact]
    public void Brackets_go_inert_once_only_the_password_is_left()
    {
        var pool = new[] { "COLD", "CORD" };
        for (int seed = 0; seed < 2000; seed++)
        {
            var board = Board(seed, 2, pool: pool);
            var brackets = Hits(board, HackingHitKind.Bracket);
            if (brackets.Count < 2) continue;
            HackingGuess first = default;
            foreach (var bracket in brackets)
            {
                first = board.Select(bracket);
                if (first.Kind == HackingGuessKind.DudRemoved) break;
            }
            if (first.Kind != HackingGuessKind.DudRemoved) continue;
            Assert.Empty(Hits(board, HackingHitKind.Bracket));
            return;
        }
        Assert.Fail("no seed produced two brackets and a dud removal");
    }

    [Fact]
    public void A_bad_pool_or_word_count_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new HackingBoard(null!, 6, 4, 0));
        Assert.Throws<ArgumentException>(() => new HackingBoard(Array.Empty<string>(), 6, 4, 0));
        Assert.Throws<ArgumentException>(() => new HackingBoard(new[] { "COLD", "COLDER" }, 6, 4, 0));
        Assert.Throws<ArgumentException>(() => new HackingBoard(new[] { "COLD" }, 6, 4, 0));
        Assert.Throws<ArgumentException>(() => new HackingBoard(new[] { "ABCDEFGHIJKLM", "ABCDEFGHIJKLN" }, 6, 4, 0));
        Assert.Throws<ArgumentException>(() => new HackingBoard(Pool, 1, 4, 0));
    }

    [Fact]
    public void Duplicate_pool_words_collapse_and_a_short_pool_lowers_the_word_count()
    {
        var board = new HackingBoard(new[] { "COLD", "COLD", "CORD", "HOLD" }, 10, 4, 3);
        Assert.Equal(3, Hits(board, HackingHitKind.Word).Count);
    }
}

public class HackingDictionaryTests
{
    [Fact]
    public void Parse_upper_cases_dedupes_and_buckets_by_length_on_space_and_line_breaks()
    {
        var dictionary = HackingDictionary.Parse("cold CORD\r\nhold\ncold  Fort\n");
        Assert.Equal(new[] { "COLD", "CORD", "HOLD", "FORT" }, dictionary.OfLength(4));
        Assert.Empty(dictionary.OfLength(5));
    }

    [Fact]
    public void Parse_drops_tokens_with_non_letters_and_an_empty_file_gives_no_words()
    {
        var dictionary = HackingDictionary.Parse("cold c0rd it's ok");
        Assert.Equal(new[] { "COLD" }, dictionary.OfLength(4));
        Assert.Equal(new[] { "OK" }, dictionary.OfLength(2));
        Assert.Empty(HackingDictionary.Parse(string.Empty).OfLength(4));
        Assert.Throws<ArgumentNullException>(() => HackingDictionary.Parse(null!));
    }
}
