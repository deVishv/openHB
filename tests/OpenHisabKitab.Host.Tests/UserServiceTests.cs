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

    [Fact]
    public void Users_list_with_their_companies_in_one_read()
    {
        var a = h.NewCompany();
        var clerk = h.NewUser("clerk", BuiltInRole.DataEntry, a);
        var list = h.Users.ListWithCompanies(h.Owner.Context);
        Assert.Equal([a], list.Single(u => u.User.Name == "clerk").Companies);
        Assert.All(list, u => Assert.Equal("", u.User.PasswordHash));
        Assert.Throws<ServiceException>(() => h.Users.ListWithCompanies(clerk.Context));
    }
}
