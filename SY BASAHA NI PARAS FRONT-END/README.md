# SY BASAHA NI PARAS FRONT-END

Sy, kani ang handoff para sa front-end side sa SALRYN.

Ang goal ani nga folder simple ra: para dali nimo masabtan asa na ang project, unsay naa na, unsay pwede hilabtan, ug unsay dapat likayan para dili maguba ang backend.

SALRYN karon naa na sa UI stage, pero controlled pa. Naa nay backend foundation, checkout draft engine, product picker boundary, checkout shell, presenter, ug viewmodel. Ang front-end work karon is paglimpyo sa screen, pagtarong sa layout, ug pag-andam sa UI para sa next wiring.

Current flow:

    CheckoutShellForm
        -> CheckoutPresenter
            -> CheckoutViewModel
                -> PosCheckoutDraftTransactionBuilder

Basic rule:

    Form = display and user input
    Presenter = UI state and actions
    ViewModel = data ready ipakita sa screen
    Core = rules
    Infrastructure = database

Ayaw ibutang ang database logic sa form. Ayaw i-enable ang Complete Sale by force. Dili pa ta ana nga phase.

Basaha in order:

1. 00-BASAHA-FIRST.md
2. 01-UNSA-ANG-SALRYN.md
3. 02-AS-OF-NOW.md
4. 03-REPO-MAP.md
5. 04-BACKEND-MAP.md
6. 05-FRONT-END-MAP.md
7. 06-CHECKOUT-FILES.md
8. 07-CODE-PATTERNS.md
9. 08-RULES-SA-FRONT-END.md
10. 09-PHASE-41.md
11. 10-TASKS-SA-FRONT-END.md
12. 11-GLOSSARY.md
13. 12-AYAW-HILABTI.md
14. 13-RUN-CHECKS.md
15. 14-CHECKLIST.md
16. INDEX.md
