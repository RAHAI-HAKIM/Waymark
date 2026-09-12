# Data Protection Impact Assessment — Waymark

**Étude d'impact relative à la protection des données à caractère personnel**

Prepared under Article 45 bis 6 of Law 18-07 of 10 June 2018, as amended and supplemented by Law 25-11 of 24 July 2025\.

| Document version | 1.0 (draft) |
| :---- | :---- |
| **Date** | \[DATE\] |
| **Author / DPO** | RAHAI Mohamed Abdelhakim, Data Protection Officer, Waymark |
| **Contact** | mohamedabdelhakim.rahai@gmail.com / 0655778424 |
| **Status** | Draft — pending ANPDP consultation |
| **Review trigger** | Any material change to purposes, architecture, hosting location, or data categories |

---

## 1\. Purpose of this assessment

This document assesses the impact on the rights and freedoms of natural persons of the processing operations carried out by the Waymark retail management and decision-support system.

It is produced for three reasons:

1. Article 41 bis 1 makes advising on and monitoring data protection impact assessments a duty of the Data Protection Officer, by reference to Article 45 bis 6\.  
2. The processing involves profiling within the meaning introduced by Law 25-11, and it is appropriate to assess that risk formally even where the processing is not otherwise high-risk.  
3. It establishes, on the record, the basis for classifying Waymark's processing under the **declaration** regime (Articles 12–14) rather than the **prior authorization** regime (Articles 17–21).

---

## 2\. Description of the processing

### 2.1 The system

Waymark is a retail management system for small and medium retailers, comprising two applications:

- a **POS application**, running at checkout terminals in the retailer's premises;  
- an **Admin application**, through which the retailer manages catalogue, inventory, suppliers, staff, and reviews the outputs of an analytical engine.

The analytical engine ("Almanac") produces recommendations across five departments: Inventory, Sales & Demand, Customer, Planning, and Supply. It uses interpretable statistical and operations-research methods (regression, time-series forecasting, constraint programming) rather than opaque machine-learning models.

### 2.2 Purposes of processing

| \# | Purpose | Legal basis |
| :---- | :---- | :---- |
| P1 | Operation of point-of-sale and retail management functions on behalf of the retailer | Art. 7 — performance of a contract |
| P2 | Generation of analytical recommendations for inventory, supply, demand and planning | Art. 7 — performance of a contract |
| P3 | Optional customer analytics and personalised offers, where the retailer enables them | Art. 7 — explicit consent of the data subject |
| P4 | Management of Waymark's own commercial relationship with retailers | Art. 7 — performance of a contract |
| P5 | Product improvement using **aggregate statistics only** | Not personal data — see §2.7 |

**P3 is disabled by default** and activated only where the retailer explicitly configures it. See §5.1.

### 2.3 Roles

| Party | Role | Basis |
| :---- | :---- | :---- |
| The retailer | **Controller** (مسؤول عن المعالجة) | Determines purposes and means for their customers' and staff data |
| Waymark | **Subprocessor** (معالج من الباطن) | Processes on the retailer's behalf and instructions, under Art. 39 contract |
| Waymark | **Controller** for P4 only | Determines purposes for its own commercial data |

An Article 39 written contract (Data Processing Agreement) is concluded with each retailer before onboarding. It provides that Waymark acts only on the controller's instructions and is bound by the security obligations of Article 38\.

Feature-level configuration rests with the retailer. This is a deliberate control to prevent drift toward joint controllership.

### 2.4 Categories of data subjects

- Customers of the retailer (where the retailer enables customer records)  
- Staff of the retailer  
- Contact persons at suppliers  
- Contact persons at the retailer (P4)

### 2.5 Categories of personal data

**Customers** — name, telephone, email, join date, loyalty points, tier, credit balance, consent records, transaction history. *Address is not collected.* Removed under the minimisation principle of Article 9(c), being unnecessary to any purpose.

**Staff** — name, telephone, email, role, join date, termination date, salary, access credentials, shift records.

**Suppliers** — company name, contact person, telephone, email, address (business address; commercial data).

**Not collected:** any category of sensitive data within the meaning of Article 3\. See §5.3.

### 2.6 Architecture and data location

The system uses a **hybrid edge architecture**. All processing takes place on Algerian territory.

**Local — retailer premises:**

- Operational database replica (products, prices, inventory, batches, transactions)  
- Customer direct identifiers  
- Staff identifiers  
- The mapping table linking `customer_id` to `pseudonym_key`  
- Consent records and notice versions  
- Data subject request records

**Cloud — Algerian hosting provider, `[PROVIDER NAME, LOCATION]`:**

- Statistics tiers 2 and 3, pseudonymised  
- Transaction history keyed by pseudonym  
- Product, inventory, supplier and purchase-order data (no personal data)  
- The analytical engine  
- Encrypted backups

**The boundary between local and cloud is the pseudonymisation boundary.** No direct identifier crosses it. The mapping table never leaves the retailer's premises.

The POS application operates fully offline against local data. Engine outputs carry a last-computed timestamp and are visibly marked when stale.

### 2.7 Product improvement data (P5)

Waymark receives, for product improvement, **only aggregate statistics computed at the store before transmission**. Controls:

- No statistic is emitted where the underlying group contains fewer than **20** data subjects.  
- Metrics are drawn from a fixed, pre-declared list; arbitrary querying is not possible.  
- Minimum and maximum values are suppressed.  
- Aggregation occurs locally; individual records are never transmitted for this purpose.

No individual record, pseudonymised or otherwise, is transmitted for product improvement. Waymark considers these aggregates not to constitute personal data within the meaning of Article 3, as no data subject is identifiable from them.

### 2.8 Retention

| Data | Retention | Basis |
| :---- | :---- | :---- |
| Customer records | `[X]` months after last transaction, or on request | Art. 9(e) |
| Transaction history (pseudonymised) | `[X]` years | Accounting and commercial obligations |
| Staff records | `[X]` years after termination | Employment obligations |
| Processing logs | `[X]` years | Art. 41 bis 3 |
| Consent records | Duration of relationship \+ `[X]` years | Evidence of consent |
| All data after contract termination | `[X]` days, following export | Contractual |

Retention periods are held as configuration, not as constants, and are enforced by an automated purge process.

---

## 3\. Classification: declaration or authorization

Under Articles 12, 17, 18, 19 and 44, and as confirmed by the ANPDP's published guidance, prior authorization is required where processing involves transfer abroad, sensitive data, or interconnection of files.

| Trigger | Applies? | Reasoning |
| :---- | :---- | :---- |
| Transfer abroad (Art. 44\) | **No** | All processing and storage on Algerian territory. No cross-border flow of personal data. |
| Sensitive data (Art. 18\) | **No** | Sensitive categories are excluded by design (§5.3). |
| Interconnection of files (Art. 19\) | **No** | Tenant data is isolated. No linking of personal data across retailers or across purposes. |

**Conclusion:** the processing falls under the declaration regime of Articles 12–14.

Waymark acknowledges that the National Authority may, under Article 17, subject the processing to prior authorization following examination of the declaration, and will comply with any such decision.

---

## 4\. Assessment of risks to rights and freedoms

Severity and likelihood are assessed after the mitigations in §5.

| \# | Risk | Severity | Likelihood | Residual |
| :---- | :---- | :---- | :---- | :---- |
| R1 | Re-identification of pseudonymised data held in the cloud | High | Low | **Low** |
| R2 | Automated decision producing legal effects on a data subject (Art. 11\) | High | Very low | **Very low** |
| R3 | Inference of sensitive data from purchase history (Art. 18\) | High | Low | **Low** |
| R4 | Unauthorised access or breach of the cloud environment | High | Low | **Medium** |
| R5 | Theft or loss of local hardware holding direct identifiers | Medium | Medium | **Medium** |
| R6 | Loss of retailer data through hardware failure | Medium | Medium | **Low** |
| R7 | Retention beyond the necessary period (Arts. 9(e), 65\) | Medium | Low | **Low** |
| R8 | Function creep — data used beyond declared purposes (Art. 58\) | Medium | Low | **Low** |
| R9 | Cross-tenant data leakage | High | Low | **Low** |
| R10 | Processing without valid consent, or after withdrawal (Art. 7\) | High | Low | **Low** |
| R11 | Failure to honour rights of access, rectification or objection | Medium | Low | **Low** |
| R12 | Waymark drifting into controller role, weakening the retailer's control | Medium | Low | **Low** |

**R4 and R5 carry the highest residual risk** and warrant continued attention. R4 is inherent to any hosted service; it is contained by the fact that the cloud holds no direct identifiers, which caps the consequences of a breach. R5 is inherent to keeping identifiers on the retailer's premises, which is itself the mitigation for R1 and R4.

---

## 5\. Measures addressing the risks

### 5.1 Human intervention in decisions (R2)

Article 11 prohibits decisions producing legal effects taken solely on the basis of automated processing intended to determine a profile or assess aspects of personality.

- Every recommendation concerning a customer requires **explicit retailer approval** before it takes effect. No engine output acts autonomously on a data subject.  
- **Credit and tier decisions are informational only.** The engine may inform them; it may never make them. There is no accept/decline loop for these outputs.  
- Ordering and commercial decisions remain the retailer's responsibility, as stated in the contract.  
- Customer-facing features are **off by default**; the retailer must actively enable them.

Article 11 paragraph 3 excludes from the prohibition decisions taken in the framework of concluding or performing a contract where the data subject was able to submit observations, and decisions responding to their own requests.

### 5.2 Pseudonymisation (R1, R4, R9)

- Direct identifiers never leave the retailer's premises.  
- The engine receives pseudonymous keys only.  
- Re-identification occurs solely at the point of delivery to the POS or Admin application, locally, and is logged.  
- The mapping table is stored separately from the operational database and never synchronised.  
- Tenant data is isolated; no cross-tenant processing of personal data occurs.

**Waymark does not claim that pseudonymised data falls outside the scope of the Law.** It remains personal data under Article 3, as the data subject remains identifiable through the mapping table. Pseudonymisation is applied as a security and risk-reduction measure under Article 38\.

### 5.3 Exclusion of sensitive categories (R3)

- Product categories are flagged where they may reveal health, religious conviction, political opinion, ethnic origin, or trade union membership.  
- Where a product belongs to any flagged category, it is excluded from customer-level analytical processing. The evaluation is made across all category links, not the primary category alone.  
- Version 1 is **not offered to pharmacies** or to retailers whose principal categories reveal sensitive characteristics.  
- Segmentation operates on non-sensitive categories only.

### 5.4 Security (R4, R5, R9)

- Encryption in transit and at rest.  
- Per-tenant encryption keys and tenant isolation in the cloud environment.  
- Documented internal access control policy; access to retailer data restricted and logged.  
- Access traceability implemented through the processing log (§5.5).  
- Confidentiality undertaking signed by all personnel with access to data (Art. 40).  
- Local device security guidance provided to retailers as part of onboarding.

### 5.5 Processing logs (R4, R8, R11)

An append-only processing log is maintained on both local and cloud sides, in accordance with Article 41 bis 3, recording:

- the operation performed (collection, consultation, disclosure, transmission, erasure, and the other operations enumerated in the Article);  
- the date and time;  
- the justification;  
- where possible, the identity of the person who consulted or disclosed the data;  
- the identity of any recipient.

Logs are used exclusively for verifying the lawfulness of processing, internal control, ensuring the integrity and security of data, and the requirements of criminal procedure. They are made available to the National Authority on request.

### 5.6 Backup and recovery (R6)

- Automated nightly backup to a second local device, with status visible in the Admin application.  
- Optional encrypted backup to Algerian cloud storage, covered by the Article 39 contract.  
- One-click restore, operable by a non-technical user.

### 5.7 Retention and deletion (R7)

- Retention periods are held as configuration and enforced automatically.  
- Deletion on request recorded and actioned.  
- Export available to the retailer at any time; deletion following contract termination on the timetable in §2.8.

### 5.8 Consent and rights (R10, R11)

- Consent captured explicitly and separately for **processing** and for **direct marketing**, reflecting the distinct requirement of Article 37\.  
- Every consent event recorded with timestamp, notice version, capturing staff member and method, in an append-only record. Withdrawal is recorded the same way.  
- An objection flag (Art. 36\) is evaluated before any customer-directed output is delivered.  
- Tooling provided to the retailer for handling requests for information, access, rectification and objection (Arts. 32, 34, 35, 36), with tracking of the **ten-day** deadline for free rectification under Article 35\.  
- Notice text and version are held as data, so that consent can be evidenced against the text in force at the time.

### 5.9 Role clarity (R12)

- The Article 39 contract states that the retailer determines purposes and means.  
- Feature configuration rests with the retailer; customer features are off by default.  
- Waymark receives no direct identifiers and only aggregate statistics for product improvement.  
- Support is provided by screen sharing without data extraction.

### 5.10 Breach response (R4, R5)

- Notification to the National Authority within **five days** of becoming aware (Art. 45 bis 8).  
- Immediate notification to the retailer, as controller, on becoming aware.  
- Notification to affected data subjects in clear and simple terms where the breach is likely to result in high risk (Art. 45 bis 10).  
- All breaches documented with their effects and the corrective measures taken (Art. 45 bis 9).

---

## 6\. Compliance obligations and status

| Obligation | Article | Status |
| :---- | :---- | :---- |
| Prior declaration | 12–14 | `[PENDING]` |
| DPO designation and notification | 41 bis | `[PENDING]` |
| Record of processing activities | 41 bis 2 | `[PENDING]` |
| Processing logs | 41 bis 3 | Design complete, implementation Stage 3 |
| Article 39 contract with each retailer | 39 | Template `[PENDING]` |
| Information notice to data subjects | 32 | Draft `[PENDING]` |
| Rights procedures | 34–36 | Design complete, implementation Stage 3 |
| Security measures | 38, 30 | Design complete |
| Breach procedure | 45 bis 8–10 | `[PENDING]` |
| Notification of changes to declared information | 14 | Ongoing |

---

## 7\. Conclusion

The processing involves profiling and therefore merits formal assessment. Following the measures described in §5 — pseudonymisation with identifiers retained on the retailer's premises, mandatory human intervention in all decisions affecting data subjects, exclusion of sensitive categories, aggregate-only product improvement data, and processing entirely within Algerian territory — the residual risk to the rights and freedoms of data subjects is assessed as **low to medium**, with no residual high risk identified.

The processing does not involve transfer abroad, sensitive data, or interconnection of files, and accordingly falls under the declaration regime.

Waymark will submit this assessment to the National Authority together with a consultation request, and will comply with any determination the Authority makes as to the applicable regime.

---

## Annex A — Questions submitted to the National Authority

1. May a subprocessor deposit declarations on behalf of controllers under a mandate, and what form must that mandate take?  
2. Do the regional branches established under Article 27 bis accept the deposit of declarations and authorization requests?  
3. Are any fees payable for declaration, authorization or consultation?  
4. Is a multi-tenant hosted service with isolated tenant data, and no linking of personal data across tenants, outside the scope of "interconnection of files" under Article 19?  
5. Does the Authority provide a model contract for the purposes of Article 39?  
6. Is retail customer management eligible for simplified declaration under Article 15?  
7. Where a solo founder designates himself as Data Protection Officer, what evidence of professional qualification is expected under Article 41 bis?

---

*Prepared by RAHAI Mohamed Abdelhakim, Data Protection Officer, Waymark. This assessment reflects the state of the system as designed at the date above and will be revised on any material change.*  
