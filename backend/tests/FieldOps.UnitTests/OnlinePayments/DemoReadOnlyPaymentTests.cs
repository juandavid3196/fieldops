using FieldOps.Application.Features.DemoMode;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.PortalAccess;

namespace FieldOps.UnitTests.OnlinePayments;

/// <summary>Read-only demo: the Stripe webhook and the public payment writes stop before any store write or provider call.</summary>
public class DemoReadOnlyPaymentTests
{
    private static readonly Guid DemoOrg = Guid.NewGuid();

    private static readonly Guid OtherOrg = Guid.NewGuid();

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task Webhook_VerifiedEvent_IsAcknowledgedButOnlyAppliedOutsideTheDemo(bool demoOrganization, int applied)
    {
        var organization = demoOrganization ? DemoOrg : OtherOrg;
        var store = new RecordingStore(organization);
        var gateway = new ScriptedGateway(organization);
        var handler = new ProcessPaymentWebhookHandler(store, gateway, new NoReceipts(), TimeProvider.System, new FakeDemo());

        // Signature handling is untouched: an invalid one is still rejected.
        Assert.False(await new ProcessPaymentWebhookHandler(
            store, new ScriptedGateway(organization, valid: false), new NoReceipts(), TimeProvider.System, new FakeDemo())
            .HandleAsync("{}", "t=1,v1=bad", CancellationToken.None));

        Assert.True(await handler.HandleAsync("{}", "t=1,v1=ok", CancellationToken.None));
        Assert.Equal(applied, store.Applied);
        Assert.Equal(applied, gateway.ProviderCalls); // outside the demo the success fetches the card details once
    }

    [Fact]
    public async Task CardIntent_ForADemoInvoice_IsRefusedBeforeAnAttemptOrProviderIntentIsCreated()
    {
        var store = new RecordingStore(DemoOrg);
        var gateway = new ScriptedGateway(DemoOrg);
        var handler = new CreateCardIntentHandler(
            store, gateway, new PaymentAttemptExpirer(store, gateway, TimeProvider.System), new OpenThrottle(), TimeProvider.System, new FakeDemo());

        var outcome = await handler.HandleAsync(ResourceAccess.FromToken("token"), Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.IsType<PublicOutcome<CardIntentView>.DemoReadOnly>(outcome);
        Assert.Equal(0, store.Prepared);
        Assert.Equal(0, gateway.ProviderCalls);
    }

    private sealed class FakeDemo : IDemoProtection
    {
        public bool Enabled => true;

        public bool IsProtectedSlug(string slug) => false;

        public Task<bool> IsProtectedOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(organizationId == DemoOrg);

        public Task<bool> IsProtectedUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class OpenThrottle : IInvoiceActionThrottle
    {
        public TimeSpan? TryAcquire(InvoiceAction action, Guid invoiceId) => null;
    }

    private sealed class NoReceipts : IPaymentReceiptNotifier
    {
        public Task<PaymentReceiptStatus> SendAsync(PaymentReceiptData receipt, CancellationToken cancellationToken) =>
            Task.FromResult(PaymentReceiptStatus.Sent);
    }

    private sealed class ScriptedGateway(Guid organization, bool valid = true) : IPaymentGateway
    {
        public int ProviderCalls { get; private set; }

        public PaymentGatewayCapabilities Capabilities => new(true, "pk_test");

        public Task<GatewayIntent> CreateIntentAsync(GatewayIntentRequest request, CancellationToken cancellationToken)
        {
            ProviderCalls++;

            return Task.FromResult(new GatewayIntent("pi_1", "secret"));
        }

        public Task<string> RetrieveClientSecretAsync(string intentId, CancellationToken cancellationToken)
        {
            ProviderCalls++;

            return Task.FromResult("secret");
        }

        public Task<GatewayCancelResult> CancelIntentAsync(string intentId, CancellationToken cancellationToken)
        {
            ProviderCalls++;

            return Task.FromResult(GatewayCancelResult.Canceled);
        }

        public Task<GatewayCardDetails> GetCardDetailsAsync(string intentId, CancellationToken cancellationToken)
        {
            ProviderCalls++;

            return Task.FromResult(new GatewayCardDetails("visa", "4242"));
        }

        public GatewayWebhookParse ParseWebhook(string payload, string? signature) =>
            valid
                ? new GatewayWebhookParse.Handled(new GatewayEvent(
                    "evt_1", GatewayEventTypes.PaymentSucceeded, "pi_1", Guid.NewGuid(), organization, Guid.NewGuid(), 10m, "USD", null, 0m))
                : new GatewayWebhookParse.Invalid();
    }

    private sealed class RecordingStore(Guid organization) : IOnlinePaymentStore
    {
        public int Applied { get; private set; }

        public int Prepared { get; private set; }

        public Task<InvoiceRef?> ResolveAsync(ResourceAccess access, CancellationToken cancellationToken) =>
            Task.FromResult<InvoiceRef?>(new InvoiceRef(organization, Guid.NewGuid()));

        public Task<IReadOnlyList<ExpiredAttempt>> FindExpiredAsync(
            Guid organizationId, Guid invoiceId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExpiredAttempt>>([]);

        public Task<bool> MarkFailedIfPendingAsync(
            Guid attemptId, string category, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<CardIntentPrepared> PrepareCardIntentAsync(
            ResourceAccess access, Guid idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Prepared++;

            throw new NotSupportedException();
        }

        public Task StoreIntentAsync(Guid attemptId, string intentId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<PaymentStatusView?> GetStatusAsync(ResourceAccess access, Guid attemptId, CancellationToken cancellationToken) =>
            Task.FromResult<PaymentStatusView?>(null);

        public Task<BankNoticeResult> ReportBankTransferAsync(
            ResourceAccess access, Guid idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReviewResult> SubmitReviewAsync(
            ResourceAccess access, int rating, string? comment, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WebhookAttemptLookup?> FindAttemptAsync(GatewayEvent gatewayEvent, CancellationToken cancellationToken) =>
            Task.FromResult<WebhookAttemptLookup?>(new WebhookAttemptLookup(Guid.NewGuid(), "pending", organization));

        public Task<WebhookResult> ApplyWebhookAsync(
            GatewayEvent gatewayEvent, WebhookExtras extras, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Applied++;

            return Task.FromResult(new WebhookResult(WebhookOutcome.Applied, null));
        }

        public Task MarkReceiptSentAsync(Guid paymentId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
