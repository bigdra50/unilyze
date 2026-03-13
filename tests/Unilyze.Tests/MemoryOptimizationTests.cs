namespace Unilyze.Tests;

public class MemoryOptimizationTests
{
    // --- LinearAllocator tests ---

    [Fact]
    public void LinearAllocator_AllocateAndAccess()
    {
        var allocator = new LinearAllocator<int>();
        try
        {
            var span = allocator.Allocate(3);
            span[0] = 10;
            span[1] = 20;
            span[2] = 30;

            var view = allocator.AsSpan();
            Assert.Equal(3, view.Length);
            Assert.Equal(10, view[0]);
            Assert.Equal(20, view[1]);
            Assert.Equal(30, view[2]);
        }
        finally
        {
            allocator.Dispose();
        }
    }

    [Fact]
    public void LinearAllocator_Deallocate_MovesBack()
    {
        var allocator = new LinearAllocator<int>();
        try
        {
            allocator.Allocate(5);
            Assert.Equal(5, allocator.Count);

            allocator.Deallocate(2);
            Assert.Equal(3, allocator.Count);
        }
        finally
        {
            allocator.Dispose();
        }
    }

    [Fact]
    public void LinearAllocator_Dispose_ReturnsToPool()
    {
        var allocator = new LinearAllocator<int>();
        allocator.Allocate(10);
        allocator.Dispose();

        Assert.True(allocator.IsDisposed);

        // ref struct cannot be captured in lambda, so verify directly with try/catch
        var threw = false;
        try
        {
            allocator.Allocate(1);
        }
        catch (ObjectDisposedException)
        {
            threw = true;
        }
        Assert.True(threw);
    }

    [Fact]
    public void LinearAllocator_MultipleAllocations()
    {
        var allocator = new LinearAllocator<int>();
        try
        {
            var span1 = allocator.Allocate(2);
            span1[0] = 1;
            span1[1] = 2;

            var span2 = allocator.Allocate(3);
            span2[0] = 3;
            span2[1] = 4;
            span2[2] = 5;

            Assert.Equal(5, allocator.Count);

            var view = allocator.AsSpan();
            Assert.Equal(1, view[0]);
            Assert.Equal(2, view[1]);
            Assert.Equal(3, view[2]);
            Assert.Equal(4, view[3]);
            Assert.Equal(5, view[4]);
        }
        finally
        {
            allocator.Dispose();
        }
    }

    [Fact]
    public void LinearAllocator_Deallocate_NegativeCount_Throws()
    {
        var allocator = new LinearAllocator<int>();
        try
        {
            allocator.Allocate(3);
            var threw = false;
            try
            {
                allocator.Deallocate(-1);
            }
            catch (ArgumentOutOfRangeException)
            {
                threw = true;
            }
            Assert.True(threw);
        }
        finally
        {
            allocator.Dispose();
        }
    }

    [Fact]
    public void LinearAllocator_Deallocate_MoreThanAllocated_Throws()
    {
        var allocator = new LinearAllocator<int>();
        try
        {
            allocator.Allocate(3);
            var threw = false;
            try
            {
                allocator.Deallocate(4);
            }
            catch (ArgumentOutOfRangeException)
            {
                threw = true;
            }
            Assert.True(threw);
        }
        finally
        {
            allocator.Dispose();
        }
    }

    [Fact]
    public void LinearAllocator_DoubleDispose_Throws()
    {
        var allocator = new LinearAllocator<int>();
        allocator.Allocate(3);
        allocator.Dispose();

        var threw = false;
        try
        {
            allocator.Dispose();
        }
        catch (ObjectDisposedException)
        {
            threw = true;
        }
        Assert.True(threw);
    }

    [Fact]
    public void LinearAllocator_Clear_AfterDispose_Throws()
    {
        var allocator = new LinearAllocator<int>();
        allocator.Allocate(3);
        allocator.Dispose();

        var threw = false;
        try
        {
            allocator.Clear();
        }
        catch (ObjectDisposedException)
        {
            threw = true;
        }
        Assert.True(threw);
    }

    [Fact]
    public void LinearAllocator_Clear_ResetsTail()
    {
        var allocator = new LinearAllocator<int>();
        try
        {
            allocator.Allocate(5);
            Assert.Equal(5, allocator.Count);

            allocator.Clear();
            Assert.Equal(0, allocator.Count);
            Assert.Equal(0, allocator.AsSpan().Length);
        }
        finally
        {
            allocator.Dispose();
        }
    }

    [Fact]
    public void LinearAllocator_Deallocate_Zero_NoChange()
    {
        var allocator = new LinearAllocator<int>();
        try
        {
            allocator.Allocate(3);
            Assert.Equal(3, allocator.Count);

            allocator.Deallocate(0);
            Assert.Equal(3, allocator.Count);
        }
        finally
        {
            allocator.Dispose();
        }
    }

    [Fact]
    public void LinearAllocator_GrowsBuffer_WhenFull()
    {
        var allocator = new LinearAllocator<int>(sizeHint: 4);
        try
        {
            var span1 = allocator.Allocate(4);
            for (var i = 0; i < 4; i++) span1[i] = i;

            // This allocation exceeds the initial sizeHint, forcing buffer growth
            var span2 = allocator.Allocate(4);
            for (var i = 0; i < 4; i++) span2[i] = i + 10;

            Assert.Equal(8, allocator.Count);

            var view = allocator.AsSpan();
            Assert.Equal(8, view.Length);
            // Verify original data survived the buffer growth
            Assert.Equal(0, view[0]);
            Assert.Equal(3, view[3]);
            Assert.Equal(10, view[4]);
            Assert.Equal(13, view[7]);
        }
        finally
        {
            allocator.Dispose();
        }
    }

    // --- BloomFilter128 tests ---

    [Fact]
    public void BloomFilter128_Empty_HasZeroPopCount()
    {
        var filter = BloomFilter128.Empty;
        Assert.Equal(0, filter.PopCount());
        Assert.True(filter.IsEmpty);
    }

    [Fact]
    public void BloomFilter128_SetBit_IncreasesPopCount()
    {
        var filter = BloomFilter128.Empty
            .SetBit(0)
            .SetBit(64)
            .SetBit(127);

        Assert.Equal(3, filter.PopCount());
        Assert.False(filter.IsEmpty);
    }

    [Fact]
    public void BloomFilter128_Union_CombinesBits()
    {
        var a = BloomFilter128.Empty.SetBit(10).SetBit(20);
        var b = BloomFilter128.Empty.SetBit(30).SetBit(40);
        var union = a.Union(b);

        Assert.Equal(4, union.PopCount());
    }

    [Fact]
    public void BloomFilter128_Intersect_CommonBits()
    {
        var a = BloomFilter128.Empty.SetBit(10).SetBit(20).SetBit(30);
        var b = BloomFilter128.Empty.SetBit(20).SetBit(30).SetBit(40);
        var intersect = a.Intersect(b);

        Assert.Equal(2, intersect.PopCount());
    }

    [Fact]
    public void BloomFilter128_MightContain_ReturnsTrueForAdded()
    {
        var filter = BloomFilter128.Empty
            .Add("hello")
            .Add("world");

        Assert.True(filter.MightContain("hello"));
        Assert.True(filter.MightContain("world"));
    }

    [Fact]
    public void BloomFilter128_MightContain_ReturnsFalseForNotAdded()
    {
        var filter = BloomFilter128.Empty.Add("hello");

        // Bloom filter: false positive is possible, but for distinct values
        // with 128 bits and 3 hash functions, false positive rate is very low.
        // Test a set of strings and verify most are not matched.
        var falsePositives = 0;
        var testStrings = new[]
        {
            "xyz", "abc", "test123", "foobar", "qux",
            "alpha", "beta", "gamma", "delta", "epsilon"
        };
        foreach (var s in testStrings)
        {
            if (filter.MightContain(s)) falsePositives++;
        }

        // With 128 bits and 3 hash functions, we expect very few false positives
        Assert.True(falsePositives <= 3, $"Too many false positives: {falsePositives}/10");
    }

    [Fact]
    public void BloomFilter128_PopCount_MatchesBitCount()
    {
        var filter = BloomFilter128.Empty;
        for (var i = 0; i < 128; i += 17)
        {
            filter = filter.SetBit(i);
        }

        // Bits set at indices: 0, 17, 34, 51, 68, 85, 102, 119 = 8 bits
        Assert.Equal(8, filter.PopCount());
    }

    [Fact]
    public void BloomFilter128_EstimateSimilarity_IdenticalFilters_ReturnsOne()
    {
        var filter = BloomFilter128.Empty.Add("hello").Add("world");
        Assert.Equal(1.0, filter.EstimateSimilarity(filter));
    }

    [Fact]
    public void BloomFilter128_EstimateSimilarity_DisjointFilters_ReturnsZero()
    {
        var a = BloomFilter128.Empty.SetBit(0).SetBit(1).SetBit(2);
        var b = BloomFilter128.Empty.SetBit(64).SetBit(65).SetBit(66);
        Assert.Equal(0.0, a.EstimateSimilarity(b));
    }

    [Fact]
    public void BloomFilter128_Equality_SameFilters_AreEqual()
    {
        var a = BloomFilter128.Empty.SetBit(42);
        var b = BloomFilter128.Empty.SetBit(42);
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void BloomFilter128_Add_EmptyString_NoError()
    {
        // Should not throw; empty string hashes to 0 for all multipliers,
        // so all 3 hash functions map to bit 0 → exactly 1 bit set
        var filter = BloomFilter128.Empty.Add("");
        Assert.True(filter.MightContain(""));
    }

    [Fact]
    public void BloomFilter128_SetBit_BoundaryBits_0And127()
    {
        var filter = BloomFilter128.Empty
            .SetBit(0)
            .SetBit(127);

        Assert.Equal(2, filter.PopCount());
    }

    [Fact]
    public void BloomFilter128_EstimateSimilarity_BothEmpty_ReturnsOne()
    {
        var a = BloomFilter128.Empty;
        var b = BloomFilter128.Empty;
        Assert.Equal(1.0, a.EstimateSimilarity(b));
    }

    [Fact]
    public void BloomFilter128_Add_UnicodeString()
    {
        var filter = BloomFilter128.Empty.Add("日本語");
        Assert.True(filter.MightContain("日本語"));
    }
}
