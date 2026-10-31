namespace OpenHisabKitab.Host.Tests;

public sealed class UserServiceTests : IDisposable
{
    readonly TestHost h = new();

    public void Dispose() => h.Dispose();

    [Fact]
    public void Companies_of_a_user_are_listed_for_the_owner_only()
    {
        var a = h.NewCompany();
        var b = h.NewCompany(TestHost.Profile("B Traders"));
        var clerk = h.NewUser("clerk", BuiltInRole.DataEntry, b);
        Assert.Equal([b], h.Users.Companies(h.Owner.Context, clerk.Context.UserId));
        h.Users.SetCompanies(h.Owner.Context, clerk.Context.UserId, [a, b]);
        Assert.Equal([a, b], h.Users.Companies(h.Owner.Context, clerk.Context.UserId).Order());
        var other = h.NewUser("other", BuiltInRole.Accountant, a);
        var ex = Assert.Throws<ServiceException>(() => h.Users.Companies(other.Context, clerk.Context.UserId));
        Assert.Equal(ServiceError.PermissionDenied, ex.Error);
    }
}
