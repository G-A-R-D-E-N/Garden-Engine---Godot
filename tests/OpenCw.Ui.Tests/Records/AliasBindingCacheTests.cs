using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.TextTags;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class AliasBindingCacheTests
{
  private static (ReferenceTagResolvers Resolvers, AliasBinder Binder) EmptyPair()
  {
    var binder = new AliasBinder([], _ => false, _ => null);
    return (new ReferenceTagResolvers(binder, EmptyNames.Instance, binder), binder);
  }

  [Fact]
  public void Resolve_without_load_order_never_builds()
  {
    var cache = new AliasBindingCache();
    var builds = 0;
    Assert.Null(cache.Resolve(false, 1, new object(), new object(), new object(), _ => { builds++; return EmptyPair(); }));
    Assert.Equal(0, builds);
    Assert.Equal(-1, cache.SessionForTests);
  }

  [Fact]
  public void Null_active_records_yields_null_without_changing_session()
  {
    var cache = new AliasBindingCache();
    var env = new object();
    Assert.NotNull(cache.Resolve(true, 1, new object(), env, env, _ => EmptyPair()));
    Assert.Null(cache.Resolve(true, 2, null, null, env, _ => EmptyPair()));
    Assert.Equal(1, cache.SessionForTests);
  }

  [Fact]
  public void Stale_environment_yields_null_without_rebuild()
  {
    var cache = new AliasBindingCache();
    var live = new object();
    var stale = new object();
    var records = new object();
    Assert.NotNull(cache.Resolve(true, 1, records, live, live, _ => EmptyPair()));
    var builds = 0;
    Assert.Null(cache.Resolve(true, 2, records, stale, live, _ => { builds++; return EmptyPair(); }));
    Assert.Equal(0, builds);
    Assert.Equal(1, cache.SessionForTests);
  }

  [Fact]
  public void Superseded_session_rebuilds_and_reuses_build_output()
  {
    var cache = new AliasBindingCache();
    var env = new object();
    var builds = 0;
    Assert.NotNull(cache.Resolve(true, 1, new object(), env, env, _ => { builds++; return EmptyPair(); }));
    Assert.Equal(1, builds);
    Assert.NotNull(cache.Resolve(true, 2, new object(), env, env, _ => { builds++; return EmptyPair(); }));
    Assert.Equal(2, builds);
    Assert.Equal(2, cache.SessionForTests);
  }

  [Fact]
  public void Matching_session_reuses_cache_without_rebuilding()
  {
    var cache = new AliasBindingCache();
    var env = new object();
    var builds = 0;
    Assert.NotNull(cache.Resolve(true, 3, new object(), env, env, _ => { builds++; return EmptyPair(); }));
    Assert.NotNull(cache.Resolve(true, 3, null, null, env, _ => { builds++; return EmptyPair(); }));
    Assert.Equal(1, builds);
  }

  [Fact]
  public void NameFor_rejects_superseded_records_before_lookup()
  {
    var active = new object();
    var held = new object();
    var ran = false;
    Assert.Null(AliasBindingCache.NameFor(held, active, () => { ran = true; return "name"; }));
    Assert.False(ran);
    Assert.Equal("name", AliasBindingCache.NameFor(active, active, () => "name"));
  }

  [Fact]
  public void Failed_build_is_remembered_for_its_session_so_it_is_not_retried_every_call()
  {
    var cache = new AliasBindingCache();
    var env = new object();
    var builds = 0;
    (ReferenceTagResolvers Resolvers, AliasBinder Binder) Throw(object _)
    {
      builds++;
      throw new InvalidOperationException("quest alias names are unavailable");
    }

    Assert.Throws<InvalidOperationException>(() => { cache.Resolve(true, 4, new object(), env, env, Throw); });
    Assert.Null(cache.Resolve(true, 4, new object(), env, env, Throw));
    Assert.Null(cache.Resolve(true, 4, null, null, env, Throw));
    Assert.Equal(1, builds);
    Assert.Equal(4, cache.SessionForTests);

    Assert.NotNull(cache.Resolve(true, 5, new object(), env, env, _ => { builds++; return EmptyPair(); }));
    Assert.Equal(2, builds);
  }

  private sealed class EmptyNames : IQuestAliasNames
  {
    public static readonly EmptyNames Instance = new();
    public IReadOnlyList<QuestAliasName>? Aliases(string questKey) => null;
  }
}