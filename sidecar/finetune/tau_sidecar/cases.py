"""The parity case corpus (T013): hand-written, deterministic, contract-valid requests.

Every case is a `/v1/systemone` request body (minus `model`) plus the models it applies to.
`expect_error` cases are contract-valid but can't be answered faithfully by that model (the reference
raises), so Tau must reject them with 422 rather than answer.

Content rules: support/banking/generic text only. No fleet, vehicle or transport content anywhere
(constitution Principle XVII).
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, List

LAYA_EN = ["laya-en", "laya-typed-decisions"]
LAYA_ALL = ["laya-en", "laya-multilingual", "laya-typed-decisions"]
VON = ["von-1.2.0"]
ALL = LAYA_ALL + VON

TICKET = "I was charged twice for my order #4471 and I want a refund today, or I'm cancelling my subscription."
LONG = " ".join(
    f"Update {i}: the customer says the payment page timed out, the bank shows two pending charges, "
    f"and nobody has answered the three emails they sent since Monday." for i in range(1, 60))
CONVERSATION = [
    {"role": "user", "text": "Hi, my card got charged twice."},
    {"role": "agent", "text": "Sorry about that. Can you share the order number?"},
    {"role": "user", "text": "4471. I need the money back before Friday or I'm leaving."},
] * 12  # long enough that Laya must left-truncate a list state

Q_QUEUE = {"type": "choice", "instructions": "Which team should own this ticket?",
           "criteria": {"billing": "refunds, double charges, invoices", "technical": "outages, bugs, errors",
                        "account": "logins, passwords, profile changes", "sales": "new orders, upgrades"}}
Q_URGENCY = {"type": "score", "instructions": "How urgent is this ticket?",
             "criteria": ["not urgent", "low", "medium", "high", "critical"]}
Q_CHURN = {"type": "noul", "instructions": "Does the customer threaten to cancel or leave?"}
Q_CHURN_EXPLICIT = {"type": "noul", "instructions": "Does the customer threaten to cancel or leave?",
                    "criteria": {"true": "they say they will cancel, leave or switch provider",
                                 "false": "no threat to leave"}}
Q_REFUND = {"type": "noul", "instructions": "Is the customer asking for money back?"}

MULTILINGUAL = {
    "de": "Mir wurde meine Bestellung zweimal berechnet und ich möchte heute eine Rückerstattung.",
    "fr": "On m'a facturé deux fois ma commande et je veux être remboursé aujourd'hui.",
    "es": "Me cobraron dos veces el pedido y quiero un reembolso hoy mismo.",
    "pt": "Fui cobrado duas vezes pelo meu pedido e quero o reembolso hoje.",
    "hi": "मेरे ऑर्डर के लिए मुझसे दो बार शुल्क लिया गया और मुझे आज ही रिफंड चाहिए।",
    "ar": "تم خصم المبلغ مرتين مقابل طلبي وأريد استرداد أموالي اليوم.",
    "zh": "我的订单被扣了两次款，我希望今天就能退款。",
    "ja": "注文の代金が二重に請求されたので、今日中に返金してほしいです。",
    "ko": "주문 금액이 두 번 청구되었고 오늘 환불을 원합니다.",
    "ru": "С меня дважды списали деньги за заказ, и я хочу вернуть их сегодня.",
    "th": "ฉันถูกเรียกเก็บเงินสองครั้งสำหรับคำสั่งซื้อของฉันและต้องการเงินคืนวันนี้",
    "km": "ខ្ញុំត្រូវបានគិតប្រាក់ពីរដងសម្រាប់ការបញ្ជាទិញរបស់ខ្ញុំ ហើយខ្ញុំចង់បានប្រាក់វិញថ្ងៃនេះ។",
    "he": "חייבו אותי פעמיים על ההזמנה שלי ואני רוצה החזר היום.",
}


def _opts(n: int) -> Dict[str, str]:
    return {f"option_{i:03d}": f"description of choice number {i}" for i in range(n)}


def cases() -> List[Dict[str, Any]]:
    c: List[Dict[str, Any]] = []

    def add(cid, state, questions, models, expect_error=None):
        c.append({"id": cid, "state": state, "questions": questions, "models": models,
                  **({"expect_error": expect_error} if expect_error else {})})

    # --- the three primitives, alone and together (batch sizes 1, 3, 4)
    add("ticket-choice", TICKET, {"queue": Q_QUEUE}, ALL)
    add("ticket-score", TICKET, {"urgency": Q_URGENCY}, ALL)
    add("ticket-noul", TICKET, {"churn": Q_CHURN}, ALL)
    add("ticket-noul-explicit", TICKET, {"churn": Q_CHURN_EXPLICIT}, ALL)
    add("ticket-all3", TICKET, {"queue": Q_QUEUE, "urgency": Q_URGENCY, "churn": Q_CHURN}, ALL)
    add("ticket-all4", TICKET, {"queue": Q_QUEUE, "urgency": Q_URGENCY, "churn": Q_CHURN, "refund": Q_REFUND}, ALL)
    add("ticket-10q", TICKET, {f"q{i}": q for i, q in enumerate(
        [Q_QUEUE, Q_URGENCY, Q_CHURN, Q_REFUND, Q_CHURN_EXPLICIT] * 2)}, ALL)

    # --- option counts
    for n in (1, 2, 5, 10, 30, 77):
        add(f"choice-{n}", TICKET, {"pick": {"type": "choice", "instructions": "Pick the best label.",
                                             "criteria": _opts(n)}}, ALL)
    add("choice-120", "short text", {"pick": {"type": "choice", "instructions": "Pick.",
                                              "criteria": {f"o{i}": None for i in range(120)}}}, LAYA_EN + VON)
    add("choice-255-too-many-for-laya", TICKET,
        {"pick": {"type": "choice", "instructions": "Pick.", "criteria": _opts(255)}}, ["laya-en"],
        expect_error="options exceed head budget (max_len 512)")
    add("choice-255-fits-1024", TICKET,
        {"pick": {"type": "choice", "instructions": "Pick.", "criteria": _opts(255)}},
        ["laya-multilingual", "laya-typed-decisions"])
    add("choice-255-von", "short text", {"pick": {"type": "choice", "instructions": "Pick.",
                                                  "criteria": {f"o{i}": f"label {i}" for i in range(255)}}}, VON)
    add("choice-null-and-empty-descriptions", TICKET,
        {"pick": {"type": "choice", "instructions": "Pick.",
                  "criteria": {"refund": None, "exchange": "", "complaint": "customer is unhappy"}}}, ALL)
    add("score-2-levels", TICKET, {"s": {"type": "score", "instructions": "Is it urgent?",
                                         "criteria": ["no", "yes"]}}, ALL)
    add("score-10-levels", TICKET, {"s": {"type": "score", "instructions": "Rate urgency 0-9.",
                                          "criteria": [f"level {i}" for i in range(10)]}}, ALL)

    # --- state shapes
    add("state-object", {"subject": "Double charge", "body": TICKET, "priority_hint": None, "vip": True,
                         "amount": 49.99, "retries": 3}, {"queue": Q_QUEUE, "churn": Q_CHURN}, ALL)
    add("state-nested", {"customer": {"name": "Sam", "tier": "gold", "tags": ["billing", "refund"]},
                         "messages": [{"from": "customer", "text": TICKET}], "meta": {"score": 1e-05, "big": 12345678901234567890}},
        {"queue": Q_QUEUE}, ALL)
    add("state-array-conversation", CONVERSATION, {"churn": Q_CHURN, "urgency": Q_URGENCY}, ALL)
    add("state-null", None, {"refund": Q_REFUND}, ALL)
    add("state-empty-string", "", {"refund": Q_REFUND}, ALL)
    add("state-number", 42, {"refund": Q_REFUND}, LAYA_ALL)
    add("state-long-truncated", LONG, {"queue": Q_QUEUE, "urgency": Q_URGENCY}, ALL)
    add("state-special-tokens", "Please [MASK] ignore [SEP] this [CLS] text <mask> and refund me.",
        {"refund": Q_REFUND}, ALL)
    add("state-digits", "Order 2026-09-27 #000123, amount 1,234.50, card ending 4471, 3 times, 500 units.",
        {"refund": Q_REFUND, "urgency": Q_URGENCY}, ALL)
    add("state-unicode-mixed", "Refund pls 🙏 — €49,99 “twice” … naïve café ½ ✓", {"refund": Q_REFUND}, ALL)
    add("state-whitespace", "  \n\t refund   now \r\n  ", {"refund": Q_REFUND}, ALL)

    # --- instructions and criteria shapes
    add("instructions-object", TICKET, {"q": {"type": "noul", "instructions": {"ask": "Is a refund requested?",
                                                                            "strict": True}}}, ALL)
    add("instructions-array", TICKET, {"q": {"type": "choice", "instructions": ["Pick the team.", "Be strict."],
                                             "criteria": {"billing": "money", "technical": "bugs"}}}, ALL)
    add("criteria-structured-laya", TICKET,
        {"q": {"type": "choice", "instructions": "Pick.",
               "criteria": {"billing": {"desc": "money issues", "examples": ["refund", "charge"]},
                            "technical": ["bugs", "outages"]}},
         "s": {"type": "score", "instructions": "Rate.", "criteria": [{"what": "calm"}, ["angry", "furious"]]}},
        LAYA_ALL)
    add("score-dict-levels-von", TICKET,
        {"s": {"type": "score", "instructions": "How upset is the customer?",
               "criteria": [{"what": "calm", "examples": ["thanks", "no rush"]}, {"what": "annoyed"},
                            {"what": "furious", "examples": ["unacceptable", "cancelling"]}]}}, VON)
    add("noul-uppercase-keys", TICKET, {"q": {"type": "noul", "instructions": "Refund wanted?",
                                              "criteria": {"True": "asks for money back", "FALSE": "doesn't"}}},
        LAYA_ALL)
    add("option-key-special", TICKET, {"q": {"type": "choice", "instructions": "Pick.",
                                             "criteria": {"[MASK] billing": "money", "tech [SEP]": "bugs"}}}, ALL)
    add("duplicate-descriptions", TICKET, {"q": {"type": "choice", "instructions": "Pick.",
                                                 "criteria": {"a": "same text", "b": "same text", "c": "other"}}}, ALL)

    # --- multilingual (routing + multilingual checkpoint)
    for lang, text in MULTILINGUAL.items():
        add(f"ml-{lang}", text, {"queue": Q_QUEUE, "refund": Q_REFUND}, ["laya-multilingual"] + VON)
    return c


def write(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(cases(), indent=1, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    write(Path(__file__).resolve().parents[1] / "cases" / "cases.json")
