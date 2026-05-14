using Shouldly;
using Warp.Core.Data.Queries;

namespace Warp.Tests.Core;

// Determinism is the entire point of this helper — if the hash drifts across processes
// or .NET versions, every server in a multi-server cluster computes a different bigint
// for the same lock key string and the advisory lock degenerates into N independent
// locks (silent correctness break). Pin the FNV-1a 64-bit contract here so any future
// "let's swap to XxHash" / "let's just use String.GetHashCode" refactor breaks loudly.
[Trait("Category", "NoDb")]
public class AdvisoryLockKeyTests
{
    [Fact]
    public void Compute_SameInput_ReturnsSameOutput()
    {
        var a = AdvisoryLockKey.Compute("warp:scheduled-activation");
        var b = AdvisoryLockKey.Compute("warp:scheduled-activation");

        a.ShouldBe(b);
    }

    [Fact]
    public void Compute_DifferentInputs_ReturnDifferentOutputs()
    {
        var a = AdvisoryLockKey.Compute("warp:scheduled-activation");
        var b = AdvisoryLockKey.Compute("warp:message-routing");

        a.ShouldNotBe(b);
    }

    // Hard-coded FNV-1a 64-bit reference vectors. Independently computed against the
    // canonical FNV-1a algorithm (offset basis 0xCBF29CE484222325, prime 0x100000001B3,
    // UTF-8 byte input, unchecked cast to int64). Hard-coding these — instead of
    // referencing a copy of the production code in the test — means an accidental
    // algorithmic drift (parameter swap, encoding swap, signed/unsigned cast change)
    // breaks the test loudly even if the regression touches both sites in the same edit.
    [Theory]
    [InlineData("", -3750763034362895579L)]
    [InlineData("warp:scheduled-activation", 5902654581797848185L)]
    [InlineData("warp:message-routing", -6404993176065726835L)]
    [InlineData("warp:counter-aggregator", 943617416611832349L)]
    [InlineData("warp:stale-job-recovery", -3740772032522242750L)]
    [InlineData("warp:orchestration", 6406268739090989172L)]
    public void Compute_MatchesFnv1a64ReferenceVector(string key, long expected)
    {
        AdvisoryLockKey.Compute(key).ShouldBe(expected);
    }

    [Fact]
    public void Compute_NullInput_Throws()
    {
        Should.Throw<ArgumentNullException>(() => AdvisoryLockKey.Compute(null!));
    }
}
