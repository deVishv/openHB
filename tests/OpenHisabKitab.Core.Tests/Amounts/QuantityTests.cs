namespace OpenHisabKitab.Core.Tests.Amounts;

public class QuantityTests
{
    [Fact]
    public void Times_is_exact() =>
        Assert.Equal(49997.5m, new Quantity(2500).Times(new Money(19999)));
}
