import { useId, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { apiClient, type components } from "@fleetops/api-client";
import { Panel } from "@fleetops/ui";
import { checked } from "../people/api";
import { useOnline } from "../shell/Shell";

export function ComplianceRules() {
  const query = useQuery({
    queryKey: ["compliance-rules"],
    queryFn: () => apiClient.GET("/api/v1/settings/compliance-rules").then(checked),
  });
  return (
    <Panel title="Compliance rules">
      {query.isLoading && <p role="status">Loading rules...</p>}
      {query.isError && (
        <p role="alert">
          Rules could not be loaded. <button onClick={() => void query.refetch()}>Retry</button>
        </p>
      )}
      {query.data && (
        <>
          <p>
            {query.data.confirmed} of {query.data.total} confirmed
          </p>
          <p>Confirm values for your operation. Guidance does not replace checking against the tachograph.</p>
          <table className="ui-table">
            <thead>
              <tr>
                <th scope="col">Rule</th>
                <th scope="col">Value</th>
                <th scope="col">Confirmation</th>
              </tr>
            </thead>
            <tbody>
              {query.data.rules.map((rule) => (
                <RuleRow
                  key={`${rule.id}-${rule.value}-${rule.confirmed}`}
                  rule={rule}
                  onSaved={() => void query.refetch()}
                />
              ))}
            </tbody>
          </table>
        </>
      )}
    </Panel>
  );
}

function RuleRow({
  rule,
  onSaved,
}: {
  rule: components["schemas"]["ComplianceRuleView"];
  onSaved: () => void;
}) {
  const fieldId = useId();
  const online = useOnline();
  const [value, setValue] = useState(rule.value ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const save = async (confirm: boolean) => {
    setBusy(true);
    setError("");
    try {
      if (confirm)
        await apiClient
          .POST("/api/v1/settings/compliance-rules/{id}/confirm", { params: { path: { id: rule.id } } })
          .then(checked);
      else {
        const parsed = value.trim() ? JSON.parse(value) : null;
        if (
          parsed !== null &&
          (typeof parsed !== "object" ||
            Array.isArray(parsed) ||
            Object.values(parsed).some(
              (item) => typeof item !== "number" || !Number.isFinite(item) || item <= 0,
            ))
        )
          throw new Error("invalid_value");
        await apiClient
          .PUT("/api/v1/settings/compliance-rules/{id}", {
            params: { path: { id: rule.id } },
            body: { value: parsed },
          })
          .then(checked);
      }
      onSaved();
    } catch {
      setError("Could not save rule. Check the value fields and connection, then retry.");
    } finally {
      setBusy(false);
    }
  };
  return (
    <tr>
      <th scope="row">
        {rule.id}: {rule.name}
        <p className="ui-muted">{rule.guideValue}</p>
      </th>
      <td>
        <label htmlFor={fieldId}>Value for {rule.id} (JSON)</label>
        <textarea id={fieldId} value={value} onChange={(event) => setValue(event.target.value)} />
        <p className="ui-muted">Fields: {rule.valueSchema}</p>
        <button
          className="ui-button"
          disabled={busy || !online || value === (rule.value ?? "")}
          onClick={() => void save(false)}
        >
          Save value
        </button>
        {error && <p role="alert">{error}</p>}
      </td>
      <td>
        <span className="attention-pill info">
          {rule.confirmed ? "Confirmed" : "Guide value \u00b7 rule not yet confirmed"}
        </span>
        <button
          className="ui-button"
          disabled={busy || !online || rule.confirmed || !rule.value || value !== rule.value}
          onClick={() => void save(true)}
        >
          Confirm
        </button>
      </td>
    </tr>
  );
}
