/**
 * What StoreServer says, in the shapes it says it (hop 8).
 *
 * These mirror `Waymark.Contracts.Recommendations` field for field, in snake_case, because
 * that is what crosses the wire. They are hand-written for now; generating them from the C#
 * contracts is Phase 1's, and the test that keeps the contracts honest against the schema
 * already exists on the other side.
 */

export type Urgency = "quiet" | "standard" | "warning" | "critical";

export type FactorDirection = "supports" | "opposes" | "context";

/** One reason, with its number. A reason without a figure is an opinion. */
export interface BecauseFactor {
  label_key: string;
  /** Exact decimal text, never a JSON number: a number here would be a double in TypeScript. */
  value: string;
  unit: string;
  direction: FactorDirection;
}

export interface BecauseBlock {
  key: string;
  params: Record<string, string>;
  /** At most three (CLAUDE.md §5). */
  factors: BecauseFactor[];
}

export interface IntentPayload {
  command: string;
  arguments: Record<string, string>;
}

export interface RecommendationOption {
  option_id: string;
  label: string;
  display_order: number;
  payload: IntentPayload;
  projected_value: string | null;
}

export interface RecommendationEnvelope {
  recommendation_id: string;
  store_id: string;
  recommendation_type: string;
  department: string;
  urgency: Urgency;
  action_type: "binary" | "menu";
  subject_type: string;
  subject_id: string;
  headline: string;
  because: BecauseBlock;
  interval_low: string | null;
  interval_high: string | null;
  computed_at: string;
  parameter_version: number | null;
  source: string;
  minimum_required_role: string;
  status: string;
  issued_at: string;
  delivered_at: string | null;
  expires_at: string | null;
  options: RecommendationOption[];
}

export interface BoardAnswer {
  outcome: "answered" | "unknown_staff";
  staff_name: string | null;
  role_code: string | null;
  /** How many live cards are addressed above this person's rank. A count, never the cards. */
  withheld: number;
  cards: RecommendationEnvelope[];
}

export interface DecisionAnswer {
  outcome: "recorded" | "refused";
  decision_id: string | null;
  reason: string | null;
}

/**
 * Through the dev server's proxy to StoreServer (see vite.config.ts). The staff member is
 * named on every request — there is no login until Phase 1 (D-069).
 */
export async function board(staffId: string): Promise<BoardAnswer> {
  const answer = await fetch(`/api/recommendations?staff=${encodeURIComponent(staffId)}`);
  if (!answer.ok) {
    throw new Error(`StoreServer answered ${answer.status}. Is it running on :5290?`);
  }
  return (await answer.json()) as BoardAnswer;
}

export async function decide(
  recommendationId: string,
  staffId: string,
  decision: "accept" | "dismiss",
  optionId: string | null,
): Promise<DecisionAnswer> {
  const answer = await fetch("/api/recommendations/decide", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      recommendation_id: recommendationId,
      staff_id: staffId,
      decision,
      option_id: optionId,
    }),
  });
  if (!answer.ok) {
    throw new Error(`StoreServer answered ${answer.status}.`);
  }
  // A refusal arrives as a 200 with a reason, like everywhere else in the slice: "you may not"
  // is an answer, and only a server that failed is an error status.
  return (await answer.json()) as DecisionAnswer;
}
