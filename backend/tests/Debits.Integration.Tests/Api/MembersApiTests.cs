using System.Net;
using Debits.Api.Contracts;
using Debits.Application.Shops;
using Debits.Domain.Entities;

namespace Debits.Integration.Tests.Api;

public class MembersApiTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Members_ListShowsBoth_WorkerCannotManage()
    {
        var s = await fx.ShopWithWorkerAsync("Staff A");

        var members = await (await s.Worker.In(s.ShopId).GetAsync("/api/members")).ReadAsync<List<MemberResponse>>();
        members.Should().HaveCount(2);
        members[0].Role.Should().Be(MemberRole.Owner, "owners first");
        members.Single(m => m.IsMe).FirstName.Should().Be("Aziz");

        var ownerId = members.Single(m => m.Role == MemberRole.Owner).Id;
        var forbidden = await s.Worker.In(s.ShopId).PatchJsonAsync($"/api/members/{ownerId}", new UpdateMemberRequest(MemberRole.Worker));
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await forbidden.CodeAsync()).Should().Be("forbidden.owner_only");

        (await s.Worker.In(s.ShopId).GetAsync("/api/invites")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Worker.In(s.ShopId).PatchJsonAsync("/api/shop", new UpdateShopRequest("Hacked", null, null, null, null, null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LastOwner_CannotBeDemotedOrRemoved_ButCanPromoteWorker()
    {
        var s = await fx.ShopWithWorkerAsync("Staff B");
        var owner = s.Owner.In(s.ShopId);
        var members = await (await owner.GetAsync("/api/members")).ReadAsync<List<MemberResponse>>();
        var me = members.Single(m => m.IsMe);
        var worker = members.Single(m => !m.IsMe);

        var demote = await owner.PatchJsonAsync($"/api/members/{me.Id}", new UpdateMemberRequest(MemberRole.Worker));
        demote.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await demote.CodeAsync()).Should().Be("member.last_owner");

        var leave = await owner.DeleteAsync($"/api/members/{me.Id}");
        leave.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var promoted = await (await owner.PatchJsonAsync($"/api/members/{worker.Id}", new UpdateMemberRequest(MemberRole.Owner))).ReadAsync<MemberResponse>();
        promoted.Role.Should().Be(MemberRole.Owner);

        // now there are two owners: the first one may step down
        (await owner.PatchJsonAsync($"/api/members/{me.Id}", new UpdateMemberRequest(MemberRole.Worker))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync("/api/invites")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "demoted in the same session");
    }

    [Fact]
    public async Task RemovedWorker_LosesAccess()
    {
        var s = await fx.ShopWithWorkerAsync("Staff C");
        var owner = s.Owner.In(s.ShopId);
        var worker = (await (await owner.GetAsync("/api/members")).ReadAsync<List<MemberResponse>>()).Single(m => m.Role == MemberRole.Worker);

        (await owner.DeleteAsync($"/api/members/{worker.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var denied = await s.Worker.In(s.ShopId).GetAsync("/api/customers");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.CodeAsync()).Should().Be("shop.not_member");
        var me = await (await s.Worker.Client.GetAsync("/api/me")).ReadAsync<MeDto>();
        me.Shops.Should().BeEmpty();
        me.ActiveShopId.Should().BeNull();
        (await (await owner.GetAsync("/api/members")).ReadAsync<List<MemberResponse>>()).Should().ContainSingle();
    }

    [Fact]
    public async Task Invites_CreateListRevoke()
    {
        var s = await fx.ShopWithWorkerAsync("Staff D");
        var owner = s.Owner.In(s.ShopId);

        var created = await (await owner.PostJsonAsync("/api/invites", new CreateInviteRequest(MemberRole.Owner))).ReadAsync<InviteResponse>();
        created.Role.Should().Be(MemberRole.Owner);
        created.Link.Should().StartWith("https://t.me/DebitsTestBot?start=inv_");
        created.ExpiresAt.Should().Be(fx.Clock.GetUtcNow().UtcDateTime.AddDays(7));
        created.CreatedByName.Should().Be("Sarvar");

        var list = await (await owner.GetAsync("/api/invites")).ReadAsync<List<InviteResponse>>();
        list.Should().ContainSingle(i => i.Id == created.Id, "the setup invite was consumed by the worker");

        (await owner.DeleteAsync($"/api/invites/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await owner.GetAsync("/api/invites")).ReadAsync<List<InviteResponse>>()).Should().BeEmpty();
        (await owner.DeleteAsync($"/api/invites/{Guid.CreateVersion7()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShopSettings_OwnerUpdates_WorkerReads()
    {
        var s = await fx.ShopWithWorkerAsync("Staff E");
        var owner = s.Owner.In(s.ShopId);

        var shop = await (await s.Worker.In(s.ShopId).GetAsync("/api/shop")).ReadAsync<ShopDto>();
        shop.Name.Should().Be("Staff E");
        shop.MembersCount.Should().Be(2);
        shop.Timezone.Should().Be("Asia/Tashkent");

        var updated = await (await owner.PatchJsonAsync("/api/shop", new UpdateShopRequest("Yangi nom", 45, AppLanguage.Ru, "Asia/Samarkand", ReminderRecipients.Owners, 20))).ReadAsync<ShopDto>();
        updated.Name.Should().Be("Yangi nom");
        updated.DefaultDueDays.Should().Be(45);
        updated.Language.Should().Be(AppLanguage.Ru);
        updated.Timezone.Should().Be("Asia/Samarkand");
        updated.ReminderRecipients.Should().Be(ReminderRecipients.Owners);
        updated.ReminderHour.Should().Be(20);

        var badTz = await owner.PatchJsonAsync("/api/shop", new UpdateShopRequest(null, null, null, "Mars/Olympus", null, null));
        badTz.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badTz.CodeAsync()).Should().Be("shop.timezone_invalid");

        var badHour = await owner.PatchJsonAsync("/api/shop", new UpdateShopRequest(null, null, null, null, null, 25));
        badHour.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badHour.CodeAsync()).Should().Be("validation");
    }

    [Fact]
    public async Task Worker_CanLeave_LastOwnerCannot()
    {
        var s = await fx.ShopWithWorkerAsync("Staff F");

        var ownerLeaves = await s.Owner.In(s.ShopId).PostAsync("/api/members/leave", null);
        ownerLeaves.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ownerLeaves.CodeAsync()).Should().Be("member.last_owner");

        (await s.Worker.In(s.ShopId).PostAsync("/api/members/leave", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await s.Worker.In(s.ShopId).GetAsync("/api/shop")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await (await s.Owner.In(s.ShopId).GetAsync("/api/members")).ReadAsync<List<MemberResponse>>()).Should().ContainSingle();
    }
}
