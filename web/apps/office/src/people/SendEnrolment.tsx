import { useMutation } from "@tanstack/react-query";
import { apiClient } from "@fleetops/api-client";
import { en } from "../i18n/en";

export function SendEnrolment({ id, name, label }: { id: string; name: string; label?: string }) {
  const send = useMutation({
    mutationFn: async () => {
      const result = await apiClient.POST("/api/v1/people/{id}/enrolment", { params: { path: { id } } });
      if (!result.response.ok || !result.data) {
        throw new Error(result.response.status === 409 ? en.enrolment.refusal : en.enrolment.failed);
      }
      return result.data;
    },
  });
  return (
    <div>
      <button type="button" className="ui-button" disabled={send.isPending} onClick={() => send.mutate()}>
        {send.isPending ? en.enrolment.sending : (label ?? en.enrolment.send)}
      </button>
      {send.isError && <p role="alert">{send.error.message}</p>}
      {send.isSuccess && (
        <div role="status">
          <p>{en.enrolment.sent(name)}</p>
          {send.data.testInbox && (
            <section aria-label={en.enrolment.inboxLabel}>
              {en.enrolment.inbox(send.data.testInbox.code, send.data.testInbox.personnelNumber)}
            </section>
          )}
        </div>
      )}
    </div>
  );
}
