using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FleetOps.Api.Auth;
using FleetOps.Identity;
using FleetOps.Migrations;
using FleetOps.People;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FleetOps.Tests;

public sealed class SignInTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory = factory;

    [Fact] public async Task Sign_in_sets_a_session_cookie_with_all_required_attributes()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var response=await _factory.SignIn(_factory.Client,employee); Assert.Equal(HttpStatusCode.OK,response.StatusCode); var c=response.Headers.GetValues("Set-Cookie").Single(); Assert.StartsWith("__Host-",c); Assert.Contains("HttpOnly",c,StringComparison.OrdinalIgnoreCase); Assert.Contains("Secure",c,StringComparison.OrdinalIgnoreCase); Assert.Contains("SameSite=Strict",c,StringComparison.OrdinalIgnoreCase); Assert.Contains("Path=/",c,StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("Domain=",c,StringComparison.OrdinalIgnoreCase); }

    [Fact] public async Task Session_token_is_stored_only_as_a_hash()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var response=await _factory.SignIn(_factory.Client,employee); var raw=Cookie(response); var sessionResponse=await Session(employee); var expected=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))); Assert.NotEqual(raw,sessionResponse.TokenHash); Assert.Equal(expected,sessionResponse.TokenHash); Assert.DoesNotContain(raw,sessionResponse.Device,StringComparison.Ordinal); }

    [Fact] public async Task Wrong_password_and_unknown_email_give_identical_responses()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var a=await _factory.SignIn(_factory.Client,employee,"wrong"); using var b=await _factory.Send(_factory.Client,HttpMethod.Post,"/api/v1/auth/sign-in",new AuthEndpoints.SignInRequest("unknown@example.test","wrong")); Assert.Equal(a.StatusCode,b.StatusCode); Assert.Equal(await a.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),await b.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)); }

    [Fact] public async Task Five_failures_lock_the_account()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); for(var i=0;i<5;i++) using(var response=await _factory.SignIn(_factory.Client,employee,"wrong")) Assert.Contains(response.StatusCode,new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Locked}); using var right=await _factory.SignIn(_factory.Client,employee); Assert.Equal(HttpStatusCode.Locked,right.StatusCode); Assert.Equal("account_locked",Code(await right.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); }

    [Fact] public async Task Lockout_clears_after_the_lockout_time()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); for(var i=0;i<5;i++){using var response=await _factory.SignIn(_factory.Client,employee,"wrong");} _factory.Clock.Advance(TimeSpan.FromMinutes(15)+TimeSpan.FromSeconds(1)); using var ok=await _factory.SignIn(_factory.Client,employee); Assert.Equal(HttpStatusCode.OK,ok.StatusCode); }

    [Fact]
    public async Task Twelve_character_passphrase_is_accepted_and_shorter_is_refused()
    {
        var acceptedUser = await _factory.CreateEmployee(["dispatcher"]);
        var acceptedToken = await ResetToken(acceptedUser);
        using var acceptedResult = await Reset(acceptedToken, "twelve chars");

        Assert.Equal(HttpStatusCode.OK, acceptedResult.StatusCode);

        var refusedUser = await _factory.CreateEmployee(["dispatcher"]);
        var refusedToken = await ResetToken(refusedUser);
        using var refusedResult = await Reset(refusedToken, "short");

        Assert.Equal(HttpStatusCode.BadRequest, refusedResult.StatusCode);
        Assert.Equal("reset_password_refused", Code(await refusedResult.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
    }

    [Fact] public async Task Hr_needs_a_second_factor_before_any_access() => await Pending(["hr"]);
    [Fact] public async Task Owner_needs_a_second_factor_before_any_access() => await Pending(["owner"]);

    [Fact] public async Task Dispatcher_does_not_need_a_second_factor()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var c=_factory.Client; await Full(c,employee); using var response=await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session"); var b=await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken); Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.DoesNotContain("second_factor",b,StringComparison.OrdinalIgnoreCase); Assert.Contains("dispatcher",b,StringComparison.OrdinalIgnoreCase); }

    [Fact] public async Task Wrong_second_factor_code_is_refused()
    { var employee=await _factory.CreateEmployee(["hr"]); using var c=_factory.Client; using var sessionResponse=await _factory.SignIn(c,employee); using var response=await _factory.Send(c,HttpMethod.Post,"/api/v1/auth/second-factor",new AuthEndpoints.SecondFactorRequest("000000")); Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode); Assert.Equal("second_factor_failed",Code(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); Assert.Equal(HttpStatusCode.Unauthorized,(await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session")).StatusCode); }

    [Fact] public async Task Enrolment_is_required_before_access_for_hr_without_a_factor()
    { var employee=await _factory.CreateEmployee(["hr"]); using var c=_factory.Client; using var sessionResponse=await _factory.SignIn(c,employee); Assert.Equal("enrol_second_factor",Next(await sessionResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); Assert.Equal(HttpStatusCode.Unauthorized,(await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session")).StatusCode); using var e=await _factory.Send(c,HttpMethod.Post,"/api/v1/auth/second-factor/enrol"); Assert.Equal(HttpStatusCode.OK,e.StatusCode); Assert.Contains("secret",await e.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)); }

    [Fact] public async Task Reused_totp_code_is_refused()
    { var employee=await _factory.CreateEmployee(["hr"]); using var a=_factory.Client; using var sessionResponse=await _factory.SignIn(a,employee); using var enrol=await _factory.Send(a,HttpMethod.Post,"/api/v1/auth/second-factor/enrol"); Assert.Equal(HttpStatusCode.OK,enrol.StatusCode); _factory.Clock.Advance(TimeSpan.FromSeconds(30)); var code=await _factory.CurrentTotp(employee); using var e=await _factory.Send(a,HttpMethod.Post,"/api/v1/auth/second-factor",new AuthEndpoints.SecondFactorRequest(code)); Assert.Equal(HttpStatusCode.OK,e.StatusCode); using var b=_factory.Client; using var s2=await _factory.SignIn(b,employee); using var response=await _factory.Send(b,HttpMethod.Post,"/api/v1/auth/second-factor",new AuthEndpoints.SecondFactorRequest(code)); Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode); Assert.Equal("second_factor_failed",Code(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); }

    [Fact] public async Task First_sign_in_requires_the_privacy_notice_once()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var a=_factory.Client; using var sessionResponse=await _factory.SignIn(a,employee); Assert.Equal("privacy_notice",Next(await sessionResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); using var p=await _factory.Send(a,HttpMethod.Post,"/api/v1/auth/privacy-notice",new AuthEndpoints.PrivacyRequest("placeholder-v1")); Assert.Equal(HttpStatusCode.OK,p.StatusCode); var row=await Privacy(employee); Assert.Equal("placeholder-v1",row.Version); Assert.Equal(_factory.Clock.GetUtcNow(),row.AcknowledgedAt); using var b=_factory.Client; using var again=await _factory.SignIn(b,employee); Assert.Equal("done",Next(await again.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); }

    [Fact] public async Task Forgot_password_response_is_identical_for_known_and_unknown_email()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var a=await _factory.Send(_factory.Client,HttpMethod.Post,"/api/v1/auth/forgot-password",new AuthEndpoints.ForgotRequest(employee.Email!)); using var b=await _factory.Send(_factory.Client,HttpMethod.Post,"/api/v1/auth/forgot-password",new AuthEndpoints.ForgotRequest("unknown@example.test")); Assert.Equal(HttpStatusCode.Accepted,a.StatusCode); Assert.Equal(a.StatusCode,b.StatusCode); Assert.Equal(await a.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),await b.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)); Assert.NotNull(_factory.ReadMailbox()); }

    [Fact] public async Task Reset_token_is_single_use()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); var resetToken=await ResetToken(employee); using var a=await Reset(resetToken,"New Correct Password!"); using var b=await Reset(resetToken,"Another Password!"); Assert.Equal(HttpStatusCode.OK,a.StatusCode); Assert.Equal(HttpStatusCode.Unauthorized,b.StatusCode); Assert.Equal("reset_token_refused",Code(await b.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); }
    [Fact] public async Task Expired_reset_token_is_refused()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); var resetToken=await ResetToken(employee); _factory.Clock.Advance(TimeSpan.FromMinutes(15)+TimeSpan.FromSeconds(1)); using var response=await Reset(resetToken,"New Correct Password!"); Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode); Assert.Equal("reset_token_refused",Code(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); }
    [Fact] public async Task Reset_token_is_stored_only_as_a_hash()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); var resetToken=await ResetToken(employee); var row=await ResetRow(employee); Assert.NotEqual(resetToken,row.TokenHash); Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resetToken))),row.TokenHash); }
    [Fact] public async Task Reset_password_revokes_existing_sessions()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var c=_factory.Client; await Full(c,employee); var resetToken=await ResetToken(employee); using var response=await Reset(resetToken,"New Correct Password!"); using var session=await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session"); Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.Equal(HttpStatusCode.Unauthorized,session.StatusCode); Assert.NotNull((await Session(employee)).RevokedAt); }

    [Fact] public async Task Idle_session_is_refused_and_revoked()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var c=_factory.Client; await Full(c,employee); _factory.Clock.Advance(TimeSpan.FromMinutes(30)+TimeSpan.FromSeconds(1)); using var response=await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session"); Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode); Assert.Equal("session_expired",Code(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); Assert.NotNull((await Session(employee)).RevokedAt); }
    [Fact] public async Task Session_past_its_absolute_lifetime_is_refused()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var c=_factory.Client; await Full(c,employee); for(var i=0;i<28;i++){_factory.Clock.Advance(TimeSpan.FromMinutes(25)); using var keep=await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session"); Assert.Equal(HttpStatusCode.OK,keep.StatusCode);} _factory.Clock.Advance(TimeSpan.FromMinutes(20)+TimeSpan.FromSeconds(1)); using var response=await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session"); Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode); Assert.Equal("session_expired",Code(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); }
    [Fact] public async Task Session_of_an_employee_past_end_date_is_refused()
    { var employee=await _factory.CreateEmployee(["dispatcher"],endDate:new DateOnly(2026,11,6)); _factory.Clock.SetUtcNow(new DateTimeOffset(2026,11,6,22,30,0,TimeSpan.Zero)); using var c=_factory.Client; await Full(c,employee); Assert.Equal(HttpStatusCode.OK,(await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session")).StatusCode); _factory.Clock.SetUtcNow(new DateTimeOffset(2026,11,6,23,1,0,TimeSpan.Zero)); using var response=await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session"); Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode); Assert.Equal("session_expired",Code(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); }
    [Fact] public async Task Session_of_a_suspended_employee_is_refused() => await StatusRefused(EmployeeStatus.Suspended);
    [Fact] public async Task Session_of_a_left_employee_is_refused() => await StatusRefused(EmployeeStatus.Left);
    [Fact] public async Task Revoked_session_is_refused_on_the_next_request()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var c=_factory.Client; await Full(c,employee); await Mutate(async db=>{var sessionResponse=await db.OfficeSessions.Where(x=>x.UserId==employee.Id).OrderByDescending(x=>x.CreatedAt).FirstAsync();sessionResponse.RevokedAt=_factory.Clock.GetUtcNow();await db.SaveChangesAsync();}); using var response=await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session"); Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode); }
    [Fact] public async Task Sign_out_revokes_the_session_and_clears_the_cookie()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var c=_factory.Client; await Full(c,employee); using var response=await _factory.Send(c,HttpMethod.Post,"/api/v1/auth/sign-out"); Assert.Equal(HttpStatusCode.NoContent,response.StatusCode); Assert.Contains(response.Headers.GetValues("Set-Cookie"),x=>x.Contains("expires=",StringComparison.OrdinalIgnoreCase)); Assert.NotNull((await Session(employee)).RevokedAt); Assert.Equal(HttpStatusCode.Unauthorized,(await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session")).StatusCode); }

    [Fact] public async Task Foreign_origin_is_refused_on_an_unsafe_request() => await OriginRefused("https://evil.example",true);
    [Fact] public async Task Missing_origin_is_refused_on_an_unsafe_request() => await OriginRefused(null,true);
    [Fact] public async Task Missing_custom_header_is_refused_on_an_unsafe_request() => await OriginRefused(_factory.Origin,false);
    [Fact] public async Task Get_requests_are_not_blocked_by_the_origin_guard()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var c=_factory.Client; await Full(c,employee); using var response=await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session",false,false); Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.Contains(employee.EmployeeId.ToString(),await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)); }

    [Fact] public async Task Dispatcher_command_actor_comes_from_the_session_not_the_payload()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); var other=await _factory.CreateEmployee(["hr"]); using var c=_factory.Client; await Full(c,employee); using var response=await _factory.Send(c,HttpMethod.Post,"/__tests/dispatcher-command",new { payloadActor=other.EmployeeId }); var b=await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken); Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.Contains(employee.EmployeeId.ToString(),b); Assert.DoesNotContain(other.EmployeeId.ToString(),b); }
    [Fact] public async Task Roles_change_takes_effect_on_the_next_request()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var c=_factory.Client; await Full(c,employee); await Mutate(async db=>{var e=await db.Employees.FindAsync(employee.EmployeeId);e!.Roles=JsonSerializer.Serialize(new[]{"hr"});await db.SaveChangesAsync();}); using var response=await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session"); var b=await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken); Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.Contains("hr",b); Assert.DoesNotContain("dispatcher",b); }
    [Fact] public async Task Step_up_action_is_refused_without_a_fresh_second_factor()
    { var employee=await _factory.CreateEmployee(["dispatcher"]); using var c=_factory.Client; await Full(c,employee); using var response=await _factory.Send(c,HttpMethod.Post,"/__tests/step-up-command",new AuthEndpoints.SecondFactorRequest("000000")); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); Assert.Equal("step_up_required",Code(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); }
    [Fact] public async Task Step_up_action_passes_after_step_up()
    { var employee=await _factory.CreateEmployee(["hr"]); using var c=_factory.Client; await Full(c,employee); _factory.Clock.Advance(TimeSpan.FromSeconds(30)); using var response=await _factory.Send(c,HttpMethod.Post,"/api/v1/auth/step-up",new AuthEndpoints.SecondFactorRequest(await _factory.CurrentTotp(employee))); Assert.Equal(HttpStatusCode.OK,response.StatusCode); using var command=await _factory.Send(c,HttpMethod.Post,"/__tests/step-up-command"); Assert.Equal(HttpStatusCode.OK,command.StatusCode); Assert.NotNull((await Session(employee)).StepUpAt); }
    [Fact] public async Task Step_up_expires_after_its_window()
    { var employee=await _factory.CreateEmployee(["hr"]); using var c=_factory.Client; await Full(c,employee); _factory.Clock.Advance(TimeSpan.FromSeconds(30)); using var response=await _factory.Send(c,HttpMethod.Post,"/api/v1/auth/step-up",new AuthEndpoints.SecondFactorRequest(await _factory.CurrentTotp(employee))); Assert.Equal(HttpStatusCode.OK,response.StatusCode); _factory.Clock.Advance(TimeSpan.FromMinutes(10)+TimeSpan.FromSeconds(1)); using var command=await _factory.Send(c,HttpMethod.Post,"/__tests/step-up-command"); Assert.Equal(HttpStatusCode.Forbidden,command.StatusCode); Assert.Equal("step_up_required",Code(await command.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); }
    [Fact] public async Task Mailbox_is_unavailable_in_production_environment()
     { var employee=await _factory.CreateEmployee(["dispatcher"]); await using var p=_factory.WithWebHostBuilder(b=>b.UseEnvironment("Production")); using var c=p.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(_factory.Origin), AllowAutoRedirect = false }); using var response=await _factory.Send(c,HttpMethod.Post,"/api/v1/auth/forgot-password",new AuthEndpoints.ForgotRequest(employee.Email!)); Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode); Assert.Equal("mailbox_unavailable",Code(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))); }

    private async Task Pending(string[] roles){var employee=await _factory.CreateEmployee(roles);using var c=_factory.Client;using var sessionResponse=await _factory.SignIn(c,employee);Assert.Contains(Next(await sessionResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)),new[]{"enrol_second_factor","second_factor"});using var response=await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session");Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);}
    private async Task Full(HttpClient c,OfficeUser employee){using var sessionResponse=await _factory.SignIn(c,employee);var next=Next(await sessionResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));if(next=="privacy_notice"){using var p=await _factory.Send(c,HttpMethod.Post,"/api/v1/auth/privacy-notice",new AuthEndpoints.PrivacyRequest("placeholder-v1"));Assert.Equal(HttpStatusCode.OK,p.StatusCode);}else if(next=="enrol_second_factor"){using var e=await _factory.Send(c,HttpMethod.Post,"/api/v1/auth/second-factor/enrol");Assert.Equal(HttpStatusCode.OK,e.StatusCode);using var f=await _factory.Send(c,HttpMethod.Post,"/api/v1/auth/second-factor",new AuthEndpoints.SecondFactorRequest(await _factory.CurrentTotp(employee)));Assert.Equal(HttpStatusCode.OK,f.StatusCode);using var p=await _factory.Send(c,HttpMethod.Post,"/api/v1/auth/privacy-notice",new AuthEndpoints.PrivacyRequest("placeholder-v1"));Assert.Equal(HttpStatusCode.OK,p.StatusCode);}else if(next=="second_factor"){using var f=await _factory.Send(c,HttpMethod.Post,"/api/v1/auth/second-factor",new AuthEndpoints.SecondFactorRequest(await _factory.CurrentTotp(employee)));Assert.Equal(HttpStatusCode.OK,f.StatusCode);}Assert.Equal(HttpStatusCode.OK,(await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session")).StatusCode);}
    private async Task StatusRefused(EmployeeStatus status){var employee=await _factory.CreateEmployee(["dispatcher"]);using var c=_factory.Client;await Full(c,employee);await Mutate(async db=>{var e=await db.Employees.FindAsync(employee.EmployeeId);e!.Status=status;await db.SaveChangesAsync();});using var response=await _factory.Send(c,HttpMethod.Get,"/api/v1/auth/session");Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);Assert.Equal("session_expired",Code(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));Assert.NotNull((await Session(employee)).RevokedAt);}
    private async Task OriginRefused(string? origin,bool header){var employee=await _factory.CreateEmployee(["dispatcher"]);using var client=_factory.Client;using var signIn=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/sign-in"){Content=JsonContent.Create(new AuthEndpoints.SignInRequest(employee.Email!,"Correct Horse Battery Staple!"))};if(origin is not null)signIn.Headers.Add("Origin",origin);if(header)signIn.Headers.Add("X-FleetOps-Request","1");using var refusedSignIn=await client.SendAsync(signIn,TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.Forbidden,refusedSignIn.StatusCode);Assert.Equal("origin_refused",Code(await refusedSignIn.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));await Full(client,employee);using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/sign-out");if(origin is not null)request.Headers.Add("Origin",origin);if(header)request.Headers.Add("X-FleetOps-Request","1");using var response=await client.SendAsync(request,TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);Assert.Equal("origin_refused",Code(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));Assert.Null((await Session(employee)).RevokedAt);}
    private async Task<string> ResetToken(OfficeUser employee){using var response=await _factory.Send(_factory.Client,HttpMethod.Post,"/api/v1/auth/forgot-password",new AuthEndpoints.ForgotRequest(employee.Email!));Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);return _factory.ReadMailbox()!.Split("token=",StringSplitOptions.None).Last();}
    private Task<HttpResponseMessage> Reset(string resetToken,string p)=>_factory.Send(_factory.Client,HttpMethod.Post,"/api/v1/auth/reset-password",new AuthEndpoints.ResetRequest(resetToken,p));
    private async Task<OfficeSession> Session(OfficeUser employee)=>await Db(db=>db.OfficeSessions.Where(x=>x.UserId==employee.Id).OrderByDescending(x=>x.CreatedAt).FirstAsync());
    private async Task<PasswordResetToken> ResetRow(OfficeUser employee)=>await Db(db=>db.PasswordResetTokens.Where(x=>x.UserId==employee.Id).OrderByDescending(x=>x.ExpiresAt).FirstAsync());
    private async Task<PrivacyAcknowledgement> Privacy(OfficeUser employee)=>await Db(db=>db.PrivacyAcknowledgements.SingleAsync(x=>x.EmployeeId==employee.EmployeeId));
    private async Task<T> Db<T>(Func<FleetOpsDbContext,Task<T>> f){using var sessionResponse=_factory.Services.CreateScope();return await f(sessionResponse.ServiceProvider.GetRequiredService<FleetOpsDbContext>());}
    private Task Mutate(Func<FleetOpsDbContext,Task> f)=>Db(async db=>{await f(db);return 0;});
    private static string Cookie(HttpResponseMessage response)=>response.Headers.GetValues("Set-Cookie").Single().Split(';')[0].Split('=',2)[1];
    private static string Code(string b)=>JsonDocument.Parse(b).RootElement.GetProperty("code").GetString()!;
    private static string Next(string b)=>JsonDocument.Parse(b).RootElement.GetProperty("next").GetString()!;
}
