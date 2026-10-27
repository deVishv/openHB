namespace OpenHisabKitab.Host.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Round_trip()
    {
        var h = PasswordHasher.Hash("correct horse");
        Assert.StartsWith("pbkdf2-sha256$600000$", h);
        Assert.True(PasswordHasher.Verify("correct horse", h));
        Assert.False(PasswordHasher.Verify("correct horsE", h));
    }

    [Fact]
    public void Same_password_hashes_differ() =>
        Assert.NotEqual(PasswordHasher.Hash("correct horse"), PasswordHasher.Hash("correct horse"));

    [Theory]
    [InlineData("")]
    [InlineData("pbkdf2-sha256$600000$notbase64$x")]
    [InlineData("pbkdf2-sha256$abc$AAAA$AAAA")]
    [InlineData("md5$1$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$600000$AAAA")]
    public void Tampered_hash_is_false_not_an_exception(string stored) =>
        Assert.False(PasswordHasher.Verify("anything", stored));
}
