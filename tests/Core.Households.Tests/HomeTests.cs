using HomeTracker.Core.Households;

namespace HomeTracker.Core.Households.Tests;

public class HomeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 9, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public void A_new_home_is_not_shared_until_sharing_starts()
    {
        var home = Home.CreateLocal("Amma's house", Now);
        Assert.False(home.IsShared);

        var shared = home.StartSharing();
        Assert.True(shared.IsShared);
        Assert.Equal(home.Id, shared.Id);
        Assert.Equal(32, shared.Key!.Length);
        Assert.Matches("^ht[a-z2-7]{26}$", shared.Topic!);
    }

    [Fact]
    public void Invite_round_trips_even_inside_a_forwarded_message()
    {
        var home = Home.CreateLocal("Amma's house", Now).StartSharing();
        var message = $"Join our Home Tracker home:\n{HomeInvite.Encode(home)}\nOnly share with family.";

        Assert.True(HomeInvite.TryDecode(message, Now, out var joined));
        Assert.Equal(home.Id, joined.Id);
        Assert.Equal("Amma's house", joined.Name);
        Assert.Equal(home.Key, joined.Key);
        Assert.Equal(home.Topic, joined.Topic);
        Assert.Equal(home.RelayUrl, joined.RelayUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("HTHOME1:short")]
    [InlineData("HTHOME1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Rejects_text_that_is_not_a_valid_invite(string? text)
    {
        Assert.False(HomeInvite.TryDecode(text, Now, out _));
    }

    [Fact]
    public void Cannot_invite_to_a_home_that_is_not_shared()
    {
        Assert.Throws<InvalidOperationException>(() => HomeInvite.Encode(Home.CreateLocal("Mine", Now)));
    }

    [Fact]
    public void Silent_alert_fires_once_per_silence()
    {
        var device = new FamilyDevice("h", "d", "Appa's phone", Now.AddHours(-25));

        Assert.True(device.NeedsSilentAlert(Now, TimeSpan.FromHours(24)));
        var alerted = device with { SilentAlertSent = Now };
        Assert.False(alerted.NeedsSilentAlert(Now.AddHours(5), TimeSpan.FromHours(24)));

        // The phone checks in again, then goes quiet again: a new alert is due.
        var back = alerted with { LastSeen = Now.AddHours(1) };
        Assert.True(back.NeedsSilentAlert(Now.AddHours(26), TimeSpan.FromHours(24)));
        Assert.False(back.NeedsSilentAlert(Now.AddHours(10), TimeSpan.FromHours(24)));
    }
}
