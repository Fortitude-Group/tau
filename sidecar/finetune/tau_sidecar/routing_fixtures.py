"""Routing parity fixtures (T037): states run through the real `laya.lang.analyse` and `Router._route`.

Writes `tests/fixtures/routing/routes.jsonl`: one header line (interpreter, Unicode and laya versions,
plus the reference's own stopword lists and diacritic set so the C# copy can be checked against them),
then one line per state with the full `analyse` dict and the route the stock `Router()` takes
(default="english", no task detection, no language hint). Constructing `Router` loads no model.

    PYTHONIOENCODING=utf-8 PYTHONPATH=. uv run --frozen python -m tau_sidecar.routing_fixtures

Content rules: support/banking/generic text only (constitution Principle XVII).
"""
from __future__ import annotations

import datetime
import json
import random
import sys
import unicodedata
from importlib.metadata import version
from pathlib import Path
from typing import Any, List, Tuple

from laya import lang as laya_lang
from laya.lang import analyse
from laya.router import Router

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / "tests" / "fixtures" / "routing" / "routes.jsonl"

Case = Tuple[str, Any]


def english() -> List[Case]:
    return [
        ("en-short", "I was charged twice"),
        ("en-short-2", "Refund please"),
        ("en-one-word", "Hello"),
        ("en-question", "Can you help me reset my password?"),
        ("en-ticket", "I was charged twice for my order #4471 and I want a refund today, or I'm cancelling."),
        ("en-long", " ".join(
            f"Update {i}: the customer says the payment page timed out and the bank shows two pending "
            f"charges, and nobody has answered the emails they sent since Monday." for i in range(1, 30))),
        ("en-object", {"subject": "Double charge", "body": "The card was charged twice for the same invoice."}),
        ("en-conversation", [{"role": "user", "text": "Hi, my card got charged twice."},
                             {"role": "agent", "text": "Sorry about that. Can you share the order number?"}]),
        ("en-content-words", "Password reset link expired yesterday"),
        ("en-de-facto", "De facto policy en route to Rio de Janeiro"),
        ("en-no", "No, no, no, the transfer never arrived"),
        ("en-caps", "THE ACCOUNT IS LOCKED AND I CANNOT LOG IN"),
        ("en-cafe-accent", "I paid at the café and the naïve cashier ran it twice"),
        ("en-resume", "Please find my résumé attached for the role"),
        ("en-fiance", "My fiancée and I want a joint account with overdraft"),
        ("en-cant", "can't won't shouldn't it's the bank's fault"),
        ("en-fullwidth", "ＴＨＥ ｂａｎｋ ｉｓ ｃｌｏｓｅｄ ａｎｄ ｔｈｅ ｃａｒｄ ｗａｓ ｌｏｃｋｅｄ"),
        ("en-math-bold", "𝐓𝐡𝐞 𝐜𝐚𝐫𝐝 𝐰𝐚𝐬 𝐜𝐡𝐚𝐫𝐠𝐞𝐝 𝐭𝐰𝐢𝐜𝐞"),
        ("en-kelvin", "The server room is 300K and the card is at the bank"),
        ("en-underscore", "the user_id and the order_id are in the log_file for this ticket"),
        ("en-hyphen", "the re-issued card and the long-standing direct-debit are wrong"),
    ]


def latin_languages() -> List[Case]:
    return [
        ("fr-1", "Bonjour, je veux annuler mon abonnement parce que le service est très lent."),
        ("fr-2", "Merci de rembourser les deux paiements, c'est la troisième fois ce mois."),
        ("fr-3", {"message": "Ma carte a été débitée deux fois pour la même commande"}),
        ("fr-stripped", "Bonjour, je veux annuler mon abonnement parce que le service est tres lent et je suis decu"),
        ("fr-short", "Bonjour merci"),
        ("de-1", "Mein Konto wurde zweimal belastet und ich möchte das Geld zurück."),
        ("de-2", "Ich habe die Rechnung nicht bekommen, bitte schicken Sie sie mir noch einmal."),
        ("de-3", "Wie kann ich mein Passwort ändern? Es ist in der App nicht zu finden."),
        ("de-in-was", "Was ist in der Rechnung"),
        ("es-1", "Hola, quiero cancelar mi suscripción porque el servicio es muy lento."),
        ("es-2", "Me cobraron dos veces y necesito que me devuelvan el dinero hoy."),
        ("es-stripped", "Hola, quiero cancelar mi suscripcion porque el servicio es muy lento y no funciona"),
        ("es-short", "Gracias por todo"),
        ("pt-1", "Olá, fui cobrado duas vezes e quero o reembolso ainda hoje."),
        ("pt-2", "Voce pode me mandar a nota fiscal?"),
        ("pt-3", "Deu erro 500 no endpoint de login depois do update"),
        ("pt-stripped", "Nao consigo entrar na minha conta, ja tentei tudo e nada funciona"),
        ("pt-quero", "Quero cancelar"),
        ("it-1", "Ciao, mi hanno addebitato due volte e vorrei un rimborso."),
        ("it-2", "Non riesco ad accedere al mio conto, la password non funziona più."),
        ("it-3", "la fattura della settimana scorsa nella cartella"),
        ("it-stripped", "Non riesco ad accedere al mio conto perche la password non funziona piu"),
        ("nl-1", "Mijn rekening is twee keer belast en ik wil het geld terug voor vrijdag."),
        ("nl-2", "Het wachtwoord werkt niet meer, maar ik heb het niet veranderd."),
        ("ro-1", "Bună ziua, am fost taxat de două ori și vreau banii înapoi."),
        ("ro-2", "Nu pot să intru în cont, parola nu mai funcționează după actualizare."),
        ("ro-cedilla", "Nu pot sa intru in cont, parola nu mai functioneaza dupa actualizare şi ţara"),
        ("az-1", "Salam, kartımdan iki dəfə pul çıxılıb və mən geri qaytarılmasını istəyirəm."),
        ("az-2", "Bu hesab üçün şifrə işləmir, daha çox kömək lazımdır."),
        ("az-ascii", "Bu hesab ucun sifre islemir, eger siz kimi biz de var"),
        ("bn-1", "Ami amar taka ferot chai, dui bar kete niyeche"),
        ("bn-2", "Apnar app kaj korche na, ami login korte parchi na ekhon"),
        ("bn-3", "Bhai amar account ta lock hoye geche, ki korbo ekhon"),
        ("bn-short", "Ami chai"),
        ("pl-1", "Dzień dobry, moja karta została obciążona dwa razy i chcę zwrotu pieniędzy."),
        ("pl-2", "Nie mogę się zalogować do konta, hasło nie działa od wczoraj."),
        ("cs-1", "Dobrý den, moje karta byla stržena dvakrát a chci vrácení peněz."),
        ("cs-2", "Nemohu se přihlásit k účtu, heslo nefunguje od včerejška."),
        ("tr-1", "Merhaba, kartımdan iki kez ödeme alındı ve paramı geri istiyorum."),
        ("tr-2", "İstanbul şubesinde hesabımı açtım ama şifremi unuttum."),
        ("tr-dotted-i", "İADE İÇİN BAŞVURU YAPTIM AMA HİÇBİR CEVAP GELMEDİ"),
        ("tr-dotted-i-short", "İİİİ İİ"),
        ("tr-dotted-i-words", "İn İs İt İ the İ"),
        ("tr-para", "Para iade edilmedi, lütfen yardım edin para"),
        ("vi-1", "Xin chào, thẻ của tôi bị trừ tiền hai lần và tôi muốn được hoàn tiền."),
        ("vi-2", "Tôi không thể đăng nhập vào tài khoản của mình."),
        ("hu-1", "Jó napot, kétszer terhelték meg a kártyámat, kérem vissza a pénzt."),
        ("lv-1", "Labdien, mana karte tika noņemta divreiz, lūdzu atmaksājiet naudu."),
        ("hr-1", "Dobar dan, kartica mi je dvaput terećena, molim povrat novca đak."),
        ("sw-1", "Habari, kadi yangu imetozwa mara mbili na nataka kurudishiwa pesa."),
        ("id-1", "Halo, kartu saya ditagih dua kali dan saya ingin uang saya kembali."),
        ("fi-1", "Hei, korttiani veloitettiin kahdesti ja haluan rahani takaisin."),
        ("mixed-en-fr", "The invoice says le montant est de deux cents euros pour le mois"),
        ("mixed-en-es", "I need help with la factura del mes, the amount is wrong"),
        ("shared-only", "la e o la e o la"),
        ("romanian-shared", "la o un de pe ca la o"),
        ("es-de-en", "de en de en de en de en"),
        ("nfd-french", "Ma carte a été débitée deux fois pour la même commande"),
        ("nfd-german", "Mein Konto wurde zweimal belastet und ich möchte das Geld zurück"),
        ("sharp-s-capital", "STRAẞE UND HAUSNUMMER SIND FALSCH IN DER RECHNUNG"),
    ]


def scripts() -> List[Case]:
    return [
        ("greek", "Η κάρτα μου χρεώθηκε δύο φορές και θέλω επιστροφή χρημάτων."),
        ("greek-extended", "ἡ κάρτα ἐχρεώθη δὶς"),
        ("cyrillic", "Мою карту списали дважды, и я хочу вернуть деньги."),
        ("cyrillic-ext", "ⷠⷡ ꙀꙁꙂ карта"),
        ("armenian", "Իմ քարտից երկու անգամ գումար է գանձվել։"),
        ("hebrew", "הכרטיס שלי חויב פעמיים ואני רוצה החזר."),
        ("arabic", "تم خصم المبلغ من بطاقتي مرتين وأريد استرداد المال."),
        ("arabic-presentation", "ﻻﺎﺑ ﭐﭑﭒ"),
        ("persian", "کارت من دو بار شارژ شد و می‌خواهم پولم را پس بگیرم."),
        ("devanagari", "मेरे कार्ड से दो बार पैसे कट गए हैं और मुझे रिफंड चाहिए।"),
        ("bengali", "আমার কার্ড থেকে দুইবার টাকা কেটে নেওয়া হয়েছে।"),
        ("gurmukhi", "ਮੇਰੇ ਕਾਰਡ ਤੋਂ ਦੋ ਵਾਰ ਪੈਸੇ ਕੱਟੇ ਗਏ ਹਨ।"),
        ("gujarati", "મારા કાર્ડમાંથી બે વાર પૈસા કપાયા છે."),
        ("oriya", "ମୋ କାର୍ଡରୁ ଦୁଇଥର ଟଙ୍କା କଟିଛି।"),
        ("tamil", "என் அட்டையில் இருமுறை பணம் எடுக்கப்பட்டது."),
        ("telugu", "నా కార్డు నుండి రెండుసార్లు డబ్బు తీసుకున్నారు."),
        ("kannada", "ನನ್ನ ಕಾರ್ಡ್‌ನಿಂದ ಎರಡು ಬಾರಿ ಹಣ ಕಡಿತವಾಗಿದೆ."),
        ("malayalam", "എന്റെ കാർഡിൽ നിന്ന് രണ്ടുതവണ പണം ഈടാക്കി."),
        ("sinhala", "මගේ කාඩ්පතෙන් දෙවරක් මුදල් අය කර ඇත."),
        ("thai", "บัตรของฉันถูกเรียกเก็บเงินสองครั้งและฉันต้องการเงินคืน"),
        ("lao", "ບັດຂອງຂ້ອຍຖືກຫັກເງິນສອງຄັ້ງ"),
        ("tibetan", "ངའི་ཁ་ཤོག་ནས་ཐེངས་གཉིས་དངུལ་བཏོན་སོང་།"),
        ("myanmar", "ကျွန်ုပ်၏ကတ်မှ နှစ်ကြိမ် ငွေဖြတ်ခံရသည်။"),
        ("georgian", "ჩემი ბარათიდან ორჯერ ჩამოიჭრა თანხა."),
        ("ethiopic", "ካርዴ ሁለት ጊዜ ተከፍሏል እና ገንዘቤን መመለስ እፈልጋለሁ።"),
        ("khmer", "កាតរបស់ខ្ញុំត្រូវបានកាត់ប្រាក់ពីរដង។"),
        ("hangul", "제 카드에서 두 번 결제되었습니다. 환불해 주세요."),
        ("hangul-jamo", "각 ㄱㅏ"),
        ("kana", "カードが二重に請求されました。返金してください。"),
        ("hiragana", "ありがとうございます、よろしくおねがいします"),
        ("kana-ext", "ㇰㇱㇲㇳ"),
        ("han", "我的卡被扣了两次款，请退款。"),
        ("han-ext-a", "㐀㐁㐂㐃"),
        ("han-compat", "豈更車"),
        ("han-ext-b", "𠀀𠀁𠀂𠀃𠀄 𠀅𠀆"),
        ("cherokee", "ᏣᎳᎩ ᎦᏬᏂᎯᏍᏗ ᎠᏍᎦᏯ"),
        ("cherokee-lower", "ꮳꮃꭹ ꭶꮼꮒꭿ"),
        ("bopomofo", "ㄅㄆㄇㄈ ㄉㄊㄋㄌ"),
        ("halfwidth-katakana", "ｶｰﾄﾞｶﾞ ﾆｼﾞｭｳ ﾆ ｾｲｷｭｳ"),
        ("mongolian", "ᠮᠣᠩᠭᠣᠯ ᠪᠢᠴᠢᠭ"),
        ("syriac", "ܫܠܡܐ ܥܠܝܟ"),
        ("thaana", "ކާޑުން ދެ ފަހަރު"),
        ("nko", "ߒߞߏ ߞߊ߲ߜߍ"),
        ("vai", "ꕙꔤ ꕉꕜ"),
        ("hangul-and-han", "카드 결제 两次 请退款"),
        ("han-and-kana-tie", "漢字カナ"),
        ("greek-cyrillic-tie", "αβγ абв"),
        ("cyrillic-greek-tie", "абв αβγ"),
        ("latin-han-tie", "abc 漢字漢"),
        ("han-latin-tie", "漢字漢 abc"),
        ("latin-other-tie", "abc 𠀀𠀁𠀂"),
        ("devanagari-ext", "ꣲꣳꣴ नमस्ते"),
    ]


def mixed_non_latin() -> List[Case]:
    cases: List[Case] = [
        ("en-cjk-brand", "请帮我退款 iPhone 15 Pro Max order ABC123XYZ"),
        ("en-cjk-order", "Order ABCDEFGHIJKLMNOPQRSTUVWXYZ 退款"),
        ("en-cjk-long", "The customer wrote the following in the ticket and the agent replied in English: 我的卡被扣了两次款"),
        ("en-greek-symbol", "Set α to 0.05 and rerun the report for the account"),
        ("en-greek-symbols", "The ratio of α to β is higher than γ for this portfolio"),
        ("en-greek-word", "The error message says σφάλμα and the page is blank"),
        ("en-cyrillic-name", "Please call Дмитрий Петрович Савицкий about the loan application today"),
        ("en-cyrillic-accent-name", "Please call Влади́мир about the overdue account"),
        ("en-cyrillic-accent-lower", "the word влади́мир was typed in the reference field of the payment"),
        ("en-ipa", "The name is pronounced [vlɐˈdʲimʲɪr] by the customer on the phone"),
        ("en-ipa-long", "ʃʒʧʤŋɲɐɪʊəɜɔæ are IPA letters"),
        ("en-arabic-digits", "The reference number is ٣٤٥٦ and the amount is ١٢٠٠"),
        ("en-arabic-digit-single", "The reference number is ٣ and the amount is wrong"),
        ("en-thai-digits", "Account ๑๒๓๔๕ was charged twice this month"),
        ("en-devanagari-digits", "Ticket ४५६७ is still open after two weeks"),
        ("en-greek-question-mark", "Is this right; or wrong;; please check"),
        ("en-modifier-letters", "ʰʲʷ ʰʲʷ the card and the bank"),
        ("en-han-upper-none", "Contact 王 about it"),
        ("en-hangul-name", "Please forward this to 김민수 in the Seoul office about the refund"),
        ("en-hebrew-word", "The customer wrote שלום in the note field of the payment"),
        ("en-emoji", "Thanks 🙏 the refund arrived 🎉"),
        ("en-arabic-mixed", "My name is أحمد and my card was charged twice for the same order"),
        ("en-cyrillic-lower-word", "the password contains привет and it does not work"),
        ("en-combining-cyrillic-only", "Order А́ is fine and the bank said so"),
    ]
    # Sweep letter counts around the non-Latin thresholds: 0.1 / 0.2 share and 10 letters.
    latin_words = "the card was charged twice for order number and we want refund today please thanks".split()
    for latin_letters in (8, 12, 16, 20, 36, 40, 44, 45, 46, 60, 80, 90, 95, 100, 120, 200):
        for cjk in (1, 2, 3, 4, 5, 8, 9, 10, 11, 12, 20):
            words, n = [], 0
            i = 0
            while n < latin_letters:
                w = latin_words[i % len(latin_words)]
                w = w[: latin_letters - n]
                words.append(w)
                n += len(w)
                i += 1
            text = " ".join(words) + " " + ("退" * cjk)
            if (latin_letters * 7 + cjk) % 3 == 0 or cjk in (10, 11) or latin_letters in (40, 45, 90, 100):
                cases.append((f"threshold-l{latin_letters}-c{cjk}", text))
    return cases


def structure() -> List[Case]:
    deep: Any = "deepest leaf est la dans le"
    for i in range(9):
        deep = {f"level{i}": deep, f"note{i}": f"level {i} note is here and the card"}
    deep_list: Any = "Mein Konto wurde zweimal belastet und ich"
    for _ in range(8):
        deep_list = [deep_list]
    deep_fr_only: Any = "je veux annuler mon abonnement pour le mois"
    for _ in range(7):
        deep_fr_only = {"x": deep_fr_only}
    at_six: Any = "je veux annuler mon abonnement pour le mois"
    for _ in range(6):
        at_six = {"x": at_six}
    return [
        ("empty-string", ""),
        ("whitespace", "   \n\t  "),
        ("null", None),
        ("int", 42),
        ("float", 3.5),
        ("true", True),
        ("false", False),
        ("empty-object", {}),
        ("empty-array", []),
        ("numbers-only-object", {"a": 1, "b": 2.5, "c": [1, 2, 3], "d": True, "e": None}),
        ("digits-only", "1234 5678 9012 3456"),
        ("punctuation-only", "!!! ??? ... ---"),
        ("emoji-only", "🙏🎉😀💳"),
        ("emoji-zwj", "👨‍👩‍👧‍👦 🏳️‍🌈"),
        ("keys-french", {"le message est pour vous": "The card was charged twice", "données": "please refund"}),
        ("keys-cjk", {"内容": "I was charged twice for the same order", "注文": "order 4471"}),
        ("keys-only-non-latin", {"内容": 1, "注文": True}),
        ("mixed-types", {"text": "Je veux annuler", "n": 5, "flag": False, "list": ["mon abonnement", 7, None, "pour le mois"]}),
        ("list-of-strings", ["je", "veux", "annuler", "mon", "abonnement", "pour", "le", "mois"]),
        ("list-mixed-languages", ["The card was charged twice", "Je veux annuler mon abonnement pour le mois prochain"]),
        ("deep-dict", deep),
        ("deep-list", deep_list),
        ("depth-7-string", deep_fr_only),
        ("depth-6-string", at_six),
        ("nested-mixed", {"a": {"b": [{"c": "我的卡被扣了两次款"}, "and the english part"]}}),
        ("empty-leaves", ["", "", "", "the card"]),
        ("many-empty-leaves", [""] * 50 + ["je veux annuler mon abonnement"]),
        ("order-matters", {"z": "Mein Konto wurde zweimal belastet", "a": "und ich möchte das Geld zurück"}),
    ]


def long_states() -> List[Case]:
    en = "The customer says the payment page timed out and the bank shows two pending charges. "
    fr = "Bonjour, je veux annuler mon abonnement parce que le service est très lent. "
    zh = "我的卡被扣了两次款，请退款。"
    return [
        ("long-en-then-fr", en * 50 + fr * 20),
        ("long-fr-then-en", fr * 60 + en * 10),
        ("long-exactly-4000", ("a" * 3999) + " "),
        ("long-4001", "b" * 4001),
        ("long-leaves-budget", ["x" * 1999, "y" * 1999, "Je veux annuler mon abonnement pour le mois"]),
        ("long-leaves-budget-2", ["x" * 1998, "y" * 1999, "le la les des une est pour dans"]),
        ("long-leaves-budget-3", ["x" * 1999, "y" * 2000, "le la les des une est pour dans"]),
        ("long-leaves-exact", ["x" * 3998, "le la les des"]),
        ("long-many-leaves", [en] * 60),
        ("long-en-then-zh", en * 47 + zh * 10),
        ("long-zh-then-en", zh * 300 + en * 10),
        ("long-astral", "𠀀" * 3000 + " the card was charged " * 100),
        ("long-astral-cut", "the card " + "𝐀" * 4100),
        ("long-dotted-i", "İ" * 3000 + " the card and the bank "),
        ("long-diacritic-tail", "a" * 3990 + " é é é é é é é é é é"),
    ]


def identifiers() -> List[Case]:
    return [
        ("url-pt", "Veja github.com e google.com.br para o manual"),
        ("urls-only", "github.com google.com example.com.br acme.com"),
        ("email", "Contact user@acme.com or support@example.org about it"),
        ("version", "Upgrade from v1.2.3 to v2.0.1 fixed it, the app works"),
        ("usa", "The U.S.A. office and the U.K. office are closed"),
        ("sentence-final-period", "Il pacco non è arrivato. La consegna era prevista ieri."),
        ("domain-it-words", "vedi il sito della banca.it e la pagina del conto.it per la fattura"),
        ("dotted-e", "e.o e.o e.o e.o la.e o.la"),
        ("hyphen-domain", "my-bank.co.uk and -foo.bar- and a.-b"),
        ("at-chains", "a@b@c d.e.f g@h.i"),
        ("dangling-dot", "the card. the bank. the loan."),
        ("dot-space", "com . o . e . em"),
        ("ellipsis", "le... la... les... des..."),
        ("unicode-identifier", "écrivez à café.fr et à réponse@société.fr pour le remboursement"),
        ("identifier-digits", "3.14 2.71 1.41 and 1,000.00 paid"),
        ("identifier-underscore", "file_name.txt and _hidden.cfg and __init__.py"),
        ("identifier-superscript", "x².y³ and ½.¼ are values"),
    ]


def numeric_letters() -> List[Case]:
    return [
        ("superscripts", "le ² la ³ les ½"),
        ("superscripts-words", "¹ ² ³ ½"),
        ("fractions-in-french", "Je veux ½ de mon remboursement pour le ¼ du mois"),
        ("roman-numerals", "Ⅻ Ⅳ ⅻ the card and the bank"),
        ("roman-numerals-words", "Ⅻ Ⅳ ⅻ ⅳ"),
        ("circled", "① ② ③ ④ the card"),
        ("circled-letters", "Ⓐ Ⓑ ⓐ ⓑ the card"),
        ("chinese-numerals", "一二三四五 and 〇"),
        ("fullwidth-digits", "１２３４ ＡＢＣ"),
        ("vulgar-fractions", "⅓ ⅔ ⅛ le la"),
        ("digits-in-words", "c4rd w4s ch4rg3d tw1c3 le2 la3 les4 des5"),
    ]


def dotted_i() -> List[Case]:
    return [
        ("dotted-i-in", "İN THE CARD İS THE BANK"),
        ("dotted-i-diac", "İ" * 20 + "é"),
        ("dotted-i-diac-2", "İ" * 49 + "é"),
        ("dotted-i-diac-3", "a" * 48 + "é"),
        ("dotless-i", "ışık ılık ıslak ırmak"),
        ("turkish-caps", "ŞİFREMİ UNUTTUM VE HESABIMA GİREMİYORUM"),
        ("azeri-caps", "MƏN BU HESAB ÜÇÜN ŞİFRƏ İSTƏYİRƏM"),
        ("capital-diacritics", "ÉTÉ ÀÉÈ ÇA ÊTRE TRÈS BIEN POUR LE MOIS"),
        ("sigma", "ΟΔΥΣΣΕΥΣ ΣΑΣ"),
    ]


def diacritic_boundaries() -> List[Case]:
    cases: List[Case] = []
    # diac / len exactly at, just under and just over 0.02, plus exact 4-dp midpoints (1/32, 1/160, 3/32).
    for total, diac in ((50, 1), (51, 1), (49, 1), (100, 2), (101, 2), (99, 2), (32, 1), (160, 1),
                        (32, 3), (320, 1), (1600, 1), (16, 1), (80, 1), (48, 1), (96, 1), (200, 3)):
        body = ("the card " * total)[: total - diac]
        cases.append((f"diac-{diac}-of-{total}", body + "é" * diac))
        cases.append((f"diac-fr-{diac}-of-{total}", ("le la les des " * total)[: total - diac] + "é" * diac))
    return cases


def nonlatin_rounding() -> List[Case]:
    # Latin shares whose 1 - share sits on or near a 4-dp midpoint.
    cases: List[Case] = []
    for latin, han in ((31, 1), (159, 1), (29, 3), (13, 3), (3, 1), (7, 1), (15, 1), (63, 1),
                       (127, 1), (27, 5), (21, 11), (6, 2), (57, 7), (3197, 3), (799, 1)):
        cases.append((f"nl-round-{latin}-{han}", ("a" * latin) + " " + ("漢" * han)))
    return cases


def fuzz(n: int = 160, seed: int = 20260927) -> List[Case]:
    """Deterministic random mixtures of stopwords, scripts, digits, identifiers and marks."""
    rng = random.Random(seed)
    stop_words = sorted({w for words in laya_lang._STOP.values() for w in words})
    pool = [
        lambda: rng.choice(stop_words),
        lambda: rng.choice(stop_words).upper(),
        lambda: rng.choice(stop_words).capitalize(),
        lambda: rng.choice(["card", "bank", "refund", "invoice", "konto", "carte", "fattura", "conta"]),
        lambda: "".join(rng.choice("漢字退款请") for _ in range(rng.randint(1, 4))),
        lambda: "".join(rng.choice("абвгдежзик") for _ in range(rng.randint(1, 6))),
        lambda: "".join(rng.choice("АБВГДЕЖ")) + "".join(rng.choice("абвгд") for _ in range(rng.randint(0, 5))),
        lambda: "".join(rng.choice("αβγδεζ") for _ in range(rng.randint(1, 3))),
        lambda: "".join(rng.choice("éèàçñöüßøåæœšžčřłęąğışțə") for _ in range(rng.randint(1, 2))),
        lambda: str(rng.randint(0, 99999)),
        lambda: rng.choice(["a.b", "x@y.z", "v1.2", "acme.com", "e.o", "la.le", "-", ".", "@", "_"]),
        lambda: rng.choice(["İ", "ı", "́", "̈", "²", "½", "Ⅻ", "🙂", "𠀀", "ｶ", "ʲ", "ɐ", "٣", "Σ"]),
        lambda: rng.choice(["카드", "カード", "मेरे", "ካርዴ", "ᏣᎳᎩ", "ㄅㄆ"]),
    ]
    cases: List[Case] = []
    for i in range(n):
        k = rng.randint(1, 40)
        weights = [rng.random() for _ in pool]
        tokens = [rng.choices(pool, weights)[0]() for _ in range(k)]
        sep = rng.choice([" ", " ", " ", "", ", ", "\n"])
        text = sep.join(tokens)
        shape = rng.random()
        if shape < 0.7:
            state: Any = text
        elif shape < 0.85:
            half = len(tokens) // 2
            state = {"a": sep.join(tokens[:half]), "b": [sep.join(tokens[half:]), rng.randint(0, 9)]}
        else:
            state = [sep.join(tokens[j:j + 3]) for j in range(0, len(tokens), 3)]
        cases.append((f"fuzz-{i:03d}", state))
    return cases


def fuzz_latin(n: int = 140, seed: int = 927) -> List[Case]:
    """Deterministic Latin-only mixtures, to exercise the stopword margins and the diacritic rate."""
    rng = random.Random(seed)
    langs = list(laya_lang._STOP)
    content = ["card", "bank", "refund", "invoice", "konto", "carte", "fattura", "conta", "hesab", "taka",
               "rechnung", "factura", "zwrot", "účet", "şifre", "tài", "khoản"]
    cases: List[Case] = []
    for i in range(n):
        main = rng.sample(langs, rng.randint(1, 3))
        k = rng.randint(1, 18)
        tokens = []
        for _ in range(k):
            r = rng.random()
            if r < 0.6:
                tokens.append(rng.choice(sorted(laya_lang._STOP[rng.choice(main)])))
            elif r < 0.8:
                tokens.append(rng.choice(content))
            elif r < 0.87:
                tokens.append(rng.choice("éèàçñöüßøåšžčřłęąğışțəİ"))
            elif r < 0.93:
                tokens.append(rng.choice(["github.com", "a.b", "e.o", "v1.2", "x@y.z", "123", "½", "İ", "É"]))
            else:
                tokens.append(rng.choice(sorted(laya_lang._STOP["en"])))
        if rng.random() < 0.3:
            tokens = [t.upper() if rng.random() < 0.5 else t for t in tokens]
        cases.append((f"fuzz-latin-{i:03d}", " ".join(tokens)))
    return cases


def all_cases() -> List[Case]:
    cases = (english() + latin_languages() + scripts() + mixed_non_latin() + structure() + long_states()
             + identifiers() + numeric_letters() + dotted_i() + diacritic_boundaries() + nonlatin_rounding()
             + fuzz() + fuzz_latin())
    ids = [c[0] for c in cases]
    assert len(ids) == len(set(ids)), "duplicate fixture ids"
    return cases


def main() -> None:
    router = Router()
    assert router.default == "english" and not router.auto_task_detection and router.lang_guess is None
    cases = all_cases()
    OUT.parent.mkdir(parents=True, exist_ok=True)
    header = {
        "header": True,
        "python": sys.version.split()[0],
        "unidata": unicodedata.unidata_version,
        "laya": version("laya"),
        "date": datetime.date.today().isoformat(),
        "count": len(cases),
        "stop": {lg: sorted(ws) for lg, ws in laya_lang._STOP.items()},
        "diacritics": sorted(laya_lang._NON_EN_DIACRITICS),
        "shared": sorted(laya_lang._SHARED_WORDS),
    }
    routes = {"english": 0, "multilingual": 0}
    with OUT.open("w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(header, ensure_ascii=False) + "\n")
        for cid, state in cases:
            det = analyse(state)
            decision = router._route(state, questions={})
            routes[decision["model"]] += 1
            row = {"id": cid, "state": state, "analyse": det,
                   "route": {"model": decision["model"], "reason": decision["reason"]}}
            f.write(json.dumps(row, ensure_ascii=False) + "\n")
    print(f"wrote {len(cases)} states to {OUT} ({routes})")


if __name__ == "__main__":
    main()
