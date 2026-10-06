using TextBean.Models;
using TextBean.Services;

namespace TextBean.Tests;

public class TreeServiceMoveTests
{
    private static byte[] FakeDoc => [(byte)'T', (byte)'B', (byte)'X', (byte)'1', 1, 9, 9, 9];

    [Fact]
    public void 문서를_다른_폴더로_옮기면_이력도_따라간다()
    {
        using var vault = new TempVault();
        var doc = vault.WriteRaw("정보.tbx", FakeDoc);
        vault.WriteRaw(@".history\정보.tbx\opened.tbx", FakeDoc);
        var target = vault.Dir("폴더1");
        var service = new TreeService(vault.Root);

        var moved = service.Move(doc, target);

        Assert.Equal(Path.Combine(target, "정보.tbx"), moved);
        Assert.True(File.Exists(Path.Combine(vault.Root, ".history", "폴더1", "정보.tbx", "opened.tbx")));
        Assert.False(Directory.Exists(Path.Combine(vault.Root, ".history", "정보.tbx")));
    }

    [Fact]
    public void 폴더를_옮기면_하위_이력_전체가_따라간다()
    {
        using var vault = new TempVault();
        vault.WriteRaw(@"폴더A\안쪽\정보.tbx", FakeDoc);
        vault.WriteRaw(@".history\폴더A\안쪽\정보.tbx\opened.tbx", FakeDoc);
        var target = vault.Dir("폴더B");
        var service = new TreeService(vault.Root);

        service.Move(Path.Combine(vault.Root, "폴더A"), target);

        Assert.True(File.Exists(
            Path.Combine(vault.Root, ".history", "폴더B", "폴더A", "안쪽", "정보.tbx", "opened.tbx")));
        Assert.False(Directory.Exists(Path.Combine(vault.Root, ".history", "폴더A")));
    }

    [Theory]
    [InlineData(MoveRejection.SameLocation)]
    [InlineData(MoveRejection.IntoItself)]
    [InlineData(MoveRejection.NameTaken)]
    [InlineData(MoveRejection.ReservedFolder)]
    [InlineData(MoveRejection.DestinationNotFolder)]
    [InlineData(MoveRejection.SourceMissing)]
    public void 거부해야_할_이동은_CheckMove가_사유와_함께_막는다(MoveRejection expected)
    {
        using var vault = new TempVault();
        var service = new TreeService(vault.Root);
        var folderA = vault.Dir("폴더A");
        var inner = vault.Dir(@"폴더A\안쪽");
        var doc = vault.WriteRaw("정보.tbx", FakeDoc);
        var docInA = vault.WriteRaw(@"폴더A\정보.tbx", FakeDoc);
        var trash = vault.Dir(".trash");

        var check = expected switch
        {
            MoveRejection.SameLocation         => service.CheckMove(doc, vault.Root),
            MoveRejection.IntoItself           => service.CheckMove(folderA, inner),
            MoveRejection.NameTaken            => service.CheckMove(doc, folderA),
            MoveRejection.ReservedFolder       => service.CheckMove(doc, trash),
            MoveRejection.DestinationNotFolder => service.CheckMove(doc, docInA),
            MoveRejection.SourceMissing        => service.CheckMove(Path.Combine(vault.Root, "없다.tbx"), folderA),
            _ => throw new InvalidOperationException()
        };

        Assert.Equal(expected, check.Reason);
        Assert.False(check.CanMove);
        Assert.False(string.IsNullOrWhiteSpace(check.Message));   // 사용자에게 이유를 말해야 한다
    }

    [Fact]
    public void 정상_이동은_CheckMove가_허용한다()
    {
        using var vault = new TempVault();
        var service = new TreeService(vault.Root);
        var doc = vault.WriteRaw("정보.tbx", FakeDoc);
        var target = vault.Dir("폴더1");

        Assert.True(service.CheckMove(doc, target).CanMove);
    }

    [Fact]
    public void 루트_밖으로는_옮길_수_없다()
    {
        using var vault = new TempVault();
        using var other = new TempVault();
        var service = new TreeService(vault.Root);
        var doc = vault.WriteRaw("정보.tbx", FakeDoc);

        Assert.Equal(MoveRejection.OutsideRoot, service.CheckMove(doc, other.Root).Reason);
    }

    [Fact]
    public void 거부된_이동은_Move가_실행돼도_아무것도_바꾸지_않는다()
    {
        using var vault = new TempVault();
        var service = new TreeService(vault.Root);
        var folderA = vault.Dir("폴더A");
        var inner = vault.Dir(@"폴더A\안쪽");

        Assert.Throws<InvalidOperationException>(() => service.Move(folderA, inner));
        Assert.True(Directory.Exists(folderA));
        Assert.True(Directory.Exists(inner));
    }

    [Fact]
    public void 경로_길이는_가장_깊은_자손_기준으로_잰다()
    {
        using var vault = new TempVault();
        vault.WriteRaw(@"얕은\매우\깊은\경로\정보.tbx", FakeDoc);
        var service = new TreeService(vault.Root);
        var folder = Path.Combine(vault.Root, "얕은");
        const string stamp = "20260921-120000";

        var deepest = service.DeepestDerivedLength(folder, stamp);

        // 폴더 자신만 재면 검사를 통과시킨 뒤 자손 문서가 저장·삭제 불가가 된다
        Assert.True(deepest > PathRules.LongestDerivedLength(vault.Root, folder, stamp));
    }

    [Fact]
    public void 문서_하나면_깊이_계산이_자기_자신과_같다()
    {
        using var vault = new TempVault();
        var doc = vault.WriteRaw("정보.tbx", FakeDoc);
        var service = new TreeService(vault.Root);
        const string stamp = "20260921-120000";

        Assert.Equal(PathRules.LongestDerivedLength(vault.Root, doc, stamp),
                     service.DeepestDerivedLength(doc, stamp));
    }
}
