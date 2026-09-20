import { useCallback, useEffect, useState } from "react";
import {
  board as fetchBoard,
  decide,
  type BecauseFactor,
  type BoardAnswer,
  type RecommendationEnvelope,
  type Urgency,
} from "./api";

/**
 * The one page of Phase 0.5's Local Admin (hop 8): the cards this staff member may act on,
 * and one button each.
 *
 * <p>
 * It renders what the store said and nothing it worked out for itself. In particular it does
 * <b>not</b> decide who may see what — the board it is given is already filtered by
 * `CardAudience`, on the server, and a screen with its own copy of that rule is how a card
 * ends up shown to somebody who is then refused when they press the button.
 * </p>
 */

/** The label the UI puts on a reason. The engine sends keys; the sentences live here. */
const FACTOR_LABELS: Record<string, string> = {
  days_to_expiry: "Jours avant péremption",
  units_on_hand: "Quantité en rayon",
  value_at_cost: "Valeur au prix d'achat",
};

const URGENCY_LABELS: Record<Urgency, string> = {
  critical: "critique",
  warning: "attention",
  standard: "à voir",
  quiet: "pour information",
};

/** Label before colour (CLAUDE.md §6): the chip always carries a word, never a bare colour. */
function UrgencyChip({ urgency }: { urgency: Urgency }) {
  return <span className={`urgency urgency-${urgency}`}>{URGENCY_LABELS[urgency]}</span>;
}

function Because({ factors }: { factors: BecauseFactor[] }) {
  return (
    <ul className="because">
      {factors.map((factor) => (
        <li key={factor.label_key} className={factor.direction}>
          <span className="label">{FACTOR_LABELS[factor.label_key] ?? factor.label_key}</span>
          <span className="figure">
            {factor.value} {factor.unit}
          </span>
        </li>
      ))}
    </ul>
  );
}

/** Every output carries its computed-at age, and stale output is shown as stale (CLAUDE.md §5). */
function age(computedAt: string): string {
  const hours = Math.floor((Date.now() - Date.parse(computedAt)) / 3_600_000);
  if (hours < 1) return "calculé à l'instant";
  if (hours < 24) return `calculé il y a ${hours} h`;
  return `calculé il y a ${Math.floor(hours / 24)} j`;
}

function Card({
  card,
  staffId,
  onAnswered,
}: {
  card: RecommendationEnvelope;
  staffId: string;
  onAnswered: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string | null>(null);

  async function answer(decision: "accept" | "dismiss", optionId: string | null) {
    setBusy(true);
    setRefusal(null);
    try {
      const answered = await decide(card.recommendation_id, staffId, decision, optionId);
      if (answered.outcome === "refused") {
        setRefusal(answered.reason);
      } else {
        onAnswered();
      }
    } catch (failure) {
      setRefusal(failure instanceof Error ? failure.message : String(failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <article className="card">
      <UrgencyChip urgency={card.urgency} />
      <h2>{card.headline}</h2>
      <Because factors={card.because.factors} />
      <p className="age">
        {age(card.computed_at)} · {card.source}
        {card.parameter_version !== null ? ` · paramètre v${card.parameter_version}` : ""}
      </p>
      <div className="actions">
        {card.options.map((option) => (
          <button
            key={option.option_id}
            className="act"
            disabled={busy}
            onClick={() => answer("accept", option.option_id)}
          >
            {option.label}
          </button>
        ))}
        <button className="act act-quiet" disabled={busy} onClick={() => answer("dismiss", null)}>
          Écarter
        </button>
      </div>
      {refusal !== null && <p className="refusal">{refusal}</p>}
    </article>
  );
}

export default function Board() {
  // Who is looking. No login until Phase 1 (D-069), so it is typed in — and kept across
  // reloads so a hand test is not a retyping exercise.
  const [staffId, setStaffId] = useState(() => localStorage.getItem("waymark.staff") ?? "");
  const [answer, setAnswer] = useState<BoardAnswer | null>(null);
  const [failure, setFailure] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (staffId.trim() === "") {
      setAnswer(null);
      return;
    }
    try {
      setFailure(null);
      setAnswer(await fetchBoard(staffId.trim()));
    } catch (thrown) {
      setFailure(thrown instanceof Error ? thrown.message : String(thrown));
      setAnswer(null);
    }
  }, [staffId]);

  useEffect(() => {
    localStorage.setItem("waymark.staff", staffId);
    void load();
  }, [staffId, load]);

  return (
    <main className="page">
      <header className="masthead">
        <h1>Waymark — Admin local</h1>
        <label className="who">
          Identifiant du personnel
          <input value={staffId} onChange={(event) => setStaffId(event.target.value)} />
        </label>
      </header>

      {failure !== null && <p className="refusal">{failure}</p>}

      {answer?.outcome === "unknown_staff" && (
        <p className="refusal">Ce magasin n'emploie personne sous cet identifiant.</p>
      )}

      {answer?.outcome === "answered" && (
        <>
          <p className="note">
            {answer.staff_name} · {answer.role_code}
            {answer.withheld > 0 &&
              ` — ${answer.withheld} carte(s) sont adressées à un rang supérieur.`}
          </p>

          {answer.cards.map((card) => (
            <Card
              key={card.recommendation_id}
              card={card}
              staffId={staffId.trim()}
              onAnswered={load}
            />
          ))}

          {/* Nothing to report is not a positive state — it is the absence of cards, said plainly. */}
          {answer.cards.length === 0 && <p className="note">Aucune carte en attente.</p>}
        </>
      )}
    </main>
  );
}
