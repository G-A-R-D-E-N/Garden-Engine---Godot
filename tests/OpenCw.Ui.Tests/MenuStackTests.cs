using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MenuStackTests
{
    sealed class FakeMenu : IMenu
    {
        readonly List<string> _log;

        public FakeMenu(string id, List<string> log, bool modal = false, bool closeOnBack = true)
        {
            Id = id;
            _log = log;
            IsModal = modal;
            CloseOnBack = closeOnBack;
        }

        public string Id { get; }
        public bool IsModal { get; }
        public bool CloseOnBack { get; }
        public List<MenuInputAction> Received { get; } = new();
        public Func<MenuInputAction, bool>? OnAction { get; set; }
        public Action? Opened { get; set; }

        public bool HandleAction(MenuInputAction action)
        {
            Received.Add(action);
            return OnAction?.Invoke(action) ?? false;
        }

        public void OnOpened()
        {
            _log.Add(Id + ":opened");
            Opened?.Invoke();
        }

        public void OnCovered() => _log.Add(Id + ":covered");

        public void OnUncovered() => _log.Add(Id + ":uncovered");

        public void OnClosed() => _log.Add(Id + ":closed");
    }

    static (MenuStack Stack, List<string> Log, List<string> Changes) Make()
    {
        var stack = new MenuStack();
        var changes = new List<string>();
        stack.TopChanged += (old, now) => changes.Add((old?.Id ?? "-") + ">" + (now?.Id ?? "-"));
        return (stack, new List<string>(), changes);
    }

    [Fact]
    public void Push_covers_the_previous_menu_and_the_newest_menu_is_on_top()
    {
        var (stack, log, changes) = Make();
        var a = new FakeMenu("A", log);
        var b = new FakeMenu("B", log);

        Assert.True(stack.Push(a));
        Assert.True(stack.Push(b));

        Assert.Same(b, stack.Top);
        Assert.Equal(2, stack.Count);
        Assert.Equal(new[] { "A:opened", "A:covered", "B:opened" }, log.ToArray());
        Assert.Equal(new[] { "->A", "A>B" }, changes.ToArray());
    }

    [Fact]
    public void Pop_closes_the_top_and_uncovers_the_one_below()
    {
        var (stack, log, changes) = Make();
        var a = new FakeMenu("A", log);
        var b = new FakeMenu("B", log);
        stack.Push(a);
        stack.Push(b);
        log.Clear();
        changes.Clear();

        Assert.True(stack.Pop());

        Assert.Same(a, stack.Top);
        Assert.Equal(new[] { "B:closed", "A:uncovered" }, log.ToArray());
        Assert.Equal(new[] { "B>A" }, changes.ToArray());
        Assert.True(stack.Pop());
        Assert.False(stack.Pop());
        Assert.Null(stack.Top);
    }

    [Fact]
    public void Replace_swaps_the_top_without_touching_the_menu_below()
    {
        var (stack, log, changes) = Make();
        var a = new FakeMenu("A", log);
        var b = new FakeMenu("B", log);
        var c = new FakeMenu("C", log);
        stack.Push(a);
        stack.Push(b);
        log.Clear();
        changes.Clear();

        Assert.True(stack.Replace(c));

        Assert.Equal(new[] { "A", "C" }, stack.Menus.Select(m => m.Id).ToArray());
        Assert.Equal(new[] { "B:closed", "C:opened" }, log.ToArray());
        Assert.Equal(new[] { "B>C" }, changes.ToArray());
    }

    [Fact]
    public void Replace_on_an_empty_stack_pushes()
    {
        var (stack, log, _) = Make();

        Assert.True(stack.Replace(new FakeMenu("A", log)));

        Assert.Equal(1, stack.Count);
    }

    [Fact]
    public void Only_the_top_menu_receives_input()
    {
        var (stack, log, _) = Make();
        var a = new FakeMenu("A", log);
        var b = new FakeMenu("B", log) { OnAction = _ => true };
        stack.Push(a);
        stack.Push(b);

        Assert.True(stack.Dispatch(MenuInputAction.Down));

        Assert.Equal(new[] { MenuInputAction.Down }, b.Received.ToArray());
        Assert.Empty(a.Received);
    }

    [Fact]
    public void Dispatch_on_an_empty_stack_is_not_handled()
    {
        Assert.False(new MenuStack().Dispatch(MenuInputAction.Accept));
    }

    [Fact]
    public void Unhandled_back_pops_a_closable_menu()
    {
        var (stack, log, _) = Make();
        var a = new FakeMenu("A", log);
        var b = new FakeMenu("B", log);
        stack.Push(a);
        stack.Push(b);

        Assert.True(stack.Dispatch(MenuInputAction.Back));

        Assert.Same(a, stack.Top);
        Assert.Contains("B:closed", log);
    }

    [Fact]
    public void Back_handled_by_the_menu_does_not_pop()
    {
        var (stack, log, _) = Make();
        var a = new FakeMenu("A", log) { OnAction = action => action == MenuInputAction.Back };
        stack.Push(a);

        Assert.True(stack.Dispatch(MenuInputAction.Back));

        Assert.Same(a, stack.Top);
    }

    [Fact]
    public void Back_does_not_pop_a_menu_that_refuses_to_close_on_back()
    {
        var (stack, log, _) = Make();
        var a = new FakeMenu("A", log, closeOnBack: false);
        stack.Push(a);

        Assert.False(stack.Dispatch(MenuInputAction.Back));

        Assert.Same(a, stack.Top);
    }

    [Fact]
    public void Other_unhandled_actions_never_pop()
    {
        var (stack, log, _) = Make();
        stack.Push(new FakeMenu("A", log));

        Assert.False(stack.Dispatch(MenuInputAction.Accept));

        Assert.Equal(1, stack.Count);
    }

    [Fact]
    public void A_menu_that_pops_itself_in_back_handling_is_not_popped_twice()
    {
        var (stack, log, _) = Make();
        var a = new FakeMenu("A", log);
        var b = new FakeMenu("B", log);
        b.OnAction = action =>
        {
            if (action == MenuInputAction.Back) stack.Pop();
            return false;
        };
        stack.Push(a);
        stack.Push(b);

        stack.Dispatch(MenuInputAction.Back);

        Assert.Same(a, stack.Top);
        Assert.Equal(1, stack.Count);
    }

    [Fact]
    public void A_modal_menu_refuses_non_modal_pushes_but_accepts_modal_ones()
    {
        var (stack, log, _) = Make();
        var modal = new FakeMenu("M", log, modal: true);
        var plain = new FakeMenu("P", log);
        var modal2 = new FakeMenu("M2", log, modal: true);
        stack.Push(new FakeMenu("A", log));
        stack.Push(modal);

        Assert.False(stack.Push(plain));
        Assert.Equal(2, stack.Count);
        Assert.False(stack.Contains(plain));

        Assert.True(stack.Push(modal2));
        Assert.Same(modal2, stack.Top);

        stack.Pop();
        stack.Pop();
        Assert.True(stack.Push(plain));
    }

    [Fact]
    public void A_modal_top_cannot_be_replaced_by_a_non_modal_menu()
    {
        var (stack, log, _) = Make();
        var modal = new FakeMenu("M", log, modal: true);
        stack.Push(modal);

        Assert.False(stack.Replace(new FakeMenu("P", log)));

        Assert.Same(modal, stack.Top);
        Assert.True(stack.Replace(new FakeMenu("M2", log, modal: true)));
    }

    [Fact]
    public void The_same_menu_cannot_be_on_the_stack_twice()
    {
        var (stack, log, _) = Make();
        var a = new FakeMenu("A", log);
        stack.Push(a);

        Assert.False(stack.Push(a));
        Assert.False(stack.Replace(a));
        Assert.Equal(1, stack.Count);
    }

    [Fact]
    public void Removing_a_buried_menu_closes_it_without_changing_the_top()
    {
        var (stack, log, changes) = Make();
        var a = new FakeMenu("A", log);
        var b = new FakeMenu("B", log);
        stack.Push(a);
        stack.Push(b);
        log.Clear();
        changes.Clear();

        Assert.True(stack.Remove(a));

        Assert.Same(b, stack.Top);
        Assert.Equal(new[] { "A:closed" }, log.ToArray());
        Assert.Empty(changes);
        Assert.False(stack.Remove(a));
    }

    [Fact]
    public void Removing_the_top_behaves_like_pop()
    {
        var (stack, log, changes) = Make();
        var a = new FakeMenu("A", log);
        var b = new FakeMenu("B", log);
        stack.Push(a);
        stack.Push(b);
        changes.Clear();

        Assert.True(stack.Remove(b));

        Assert.Same(a, stack.Top);
        Assert.Equal(new[] { "B>A" }, changes.ToArray());
    }

    [Fact]
    public void Clear_closes_every_menu_top_first_and_reports_one_change()
    {
        var (stack, log, changes) = Make();
        stack.Push(new FakeMenu("A", log));
        stack.Push(new FakeMenu("B", log));
        stack.Push(new FakeMenu("C", log));
        log.Clear();
        changes.Clear();

        Assert.True(stack.Clear());

        Assert.Equal(0, stack.Count);
        Assert.Equal(new[] { "C:closed", "B:closed", "A:closed" }, log.ToArray());
        Assert.Equal(new[] { "C>-" }, changes.ToArray());
        Assert.False(stack.Clear());
    }

    [Fact]
    public void A_push_made_inside_on_opened_is_queued_and_applied_after_the_outer_call()
    {
        var (stack, log, _) = Make();
        var b = new FakeMenu("B", log);
        var a = new FakeMenu("A", log);
        var countInside = -1;
        var queuedResult = false;
        a.Opened = () =>
        {
            queuedResult = stack.Push(b);
            countInside = stack.Count;
        };

        stack.Push(a);

        Assert.True(queuedResult);
        Assert.Equal(1, countInside);
        Assert.Equal(new[] { "A:opened", "A:covered", "B:opened" }, log.ToArray());
        Assert.Same(b, stack.Top);
    }

    [Fact]
    public void A_push_made_inside_a_top_changed_handler_is_queued_in_order()
    {
        var (stack, log, _) = Make();
        var b = new FakeMenu("B", log);
        var pushed = false;
        stack.TopChanged += (_, now) =>
        {
            if (now?.Id == "A" && !pushed)
            {
                pushed = true;
                stack.Push(b);
            }
        };

        stack.Push(new FakeMenu("A", log));

        Assert.Equal(new[] { "A", "B" }, stack.Menus.Select(m => m.Id).ToArray());
    }

    [Fact]
    public void A_pop_requested_from_handle_action_is_applied_after_the_handler_returns()
    {
        var (stack, log, _) = Make();
        var a = new FakeMenu("A", log);
        var b = new FakeMenu("B", log);
        var countInside = -1;
        b.OnAction = _ =>
        {
            stack.Pop();
            countInside = stack.Count;
            return true;
        };
        stack.Push(a);
        stack.Push(b);

        Assert.True(stack.Dispatch(MenuInputAction.Accept));

        Assert.Equal(2, countInside);
        Assert.Same(a, stack.Top);
    }

    [Fact]
    public void Queued_operations_run_in_the_order_they_were_requested()
    {
        var (stack, log, _) = Make();
        var a = new FakeMenu("A", log);
        var b = new FakeMenu("B", log);
        var c = new FakeMenu("C", log);
        a.Opened = () =>
        {
            stack.Push(b);
            stack.Pop();
            stack.Push(c);
        };

        stack.Push(a);

        Assert.Equal(new[] { "A", "C" }, stack.Menus.Select(m => m.Id).ToArray());
        Assert.Equal(new[] { "A:opened", "A:covered", "B:opened", "B:closed", "A:uncovered", "A:covered", "C:opened" }, log.ToArray());
    }

    [Fact]
    public void A_throwing_callback_does_not_leave_the_stack_stuck_in_a_call()
    {
        var (stack, log, changes) = Make();
        var bad = new FakeMenu("bad", log) { Opened = () => throw new InvalidOperationException("boom") };

        Assert.Throws<InvalidOperationException>(() => stack.Push(bad));

        Assert.Equal(0, stack.Count);
        Assert.Null(stack.Top);
        Assert.False(stack.Contains(bad));
        Assert.Empty(changes);
        Assert.True(stack.Push(new FakeMenu("good", log)));
        Assert.Equal("good", stack.Top?.Id);
        Assert.Equal(new[] { "->good" }, changes.ToArray());
    }

    [Fact]
    public void A_push_whose_on_opened_throws_leaves_the_menu_below_on_top_and_uncovered()
    {
        var (stack, log, changes) = Make();
        var a = new FakeMenu("A", log);
        stack.Push(a);
        changes.Clear();
        log.Clear();
        var bad = new FakeMenu("bad", log) { Opened = () => throw new InvalidOperationException("boom") };

        Assert.Throws<InvalidOperationException>(() => stack.Push(bad));

        Assert.Same(a, stack.Top);
        Assert.Equal(1, stack.Count);
        Assert.Empty(changes);
        Assert.Equal(new[] { "A:covered", "bad:opened", "A:uncovered" }, log.ToArray());
    }

    [Fact]
    public void A_null_menu_is_rejected()
    {
        var stack = new MenuStack();

        Assert.Throws<ArgumentNullException>(() => stack.Push(null!));
        Assert.Throws<ArgumentNullException>(() => stack.Replace(null!));
        Assert.Throws<ArgumentNullException>(() => stack.Remove(null!));
    }
}
