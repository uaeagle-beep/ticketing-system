# Звіт з безпеки — TicketTracker

> Фінальний синтез провідного інженера з безпеки. Усі знахідки нижче пройшли пряму верифікацію проти вихідного коду; severity відкалібровано після перевірки. Звіт враховує, що це **хакатон-проєкт**, у якому RBAC / membership-ізоляція та production-grade пошта явно поза скоупом.

_Дата: 2026-06-30_

---

## 1. Executive Summary

Перевірено **16 початкових знахідок**. Після верифікації:

- **15 підтверджено** (14 CONFIRMED + 1 PARTIAL),
- **1 спростовано** (REFUTED — хибне спрацювання).

### Розподіл за фінальною severity (після верифікації)

| Severity | Кількість | Знахідки |
|----------|-----------|----------|
| **Critical** | 0 | — |
| **High** | 0 | — |
| **Medium** | 3 | AUTH-001, AUTH-002, SCT-001 |
| **Low** | 8 | AUTH-003, AUTH-004, AUTH-005, AUTH-006, INJ-01 (PARTIAL), SCT-003, SCT-004, SCT-005, SCT-006 |
| **Info** | 4 | AUTH-007, AUTH-008, SCT-007 |
| **Refuted** | 1 | SCT-002 |

> Примітка: AUTH-006 та SCT-001 описують ту саму першопричину (відкритий CORS) під різними кутами; вони залишені як окремі записи, але виправляються одним фіксом. Кількість унікальних дефектів коду, які варто полагодити, — менша за кількість рядків таблиці.

### Загальний висновок про поставу безпеки

Постава безпеки для проєкту такого масштабу **зріла та свідома**. Жодних Critical або High після верифікації: **немає прямих компрометацій облікових даних, сесій чи даних, немає робочих ін'єкцій, немає обхідних шляхів автентифікації**. Ключові примітиви реалізовані правильно:

- **Argon2id** із CSPRNG-сіллю на кожен хеш та constant-time порівнянням (`CryptographicOperations.FixedTimeEquals`), параметри в межах базової рекомендації OWASP (AUTH-008).
- **Опакові токени сесій/верифікації** — 256-бітні CSPRNG-значення; навіть несолений SHA-256 над таким входом криптографічно безпечний (AUTH-004, SCT-005).
- **Анти-енумерація на рівні тіла відповіді** реалізована послідовно (однакові 401/201/202 на login/signup/resend) — єдиний задокументований виняток (AUTH-007) є усвідомленим UX-компромісом.
- **Шлюз автентифікації коректний**: `BearerAuthMiddleware` захищає весь `/api/*` окрім задокументованого публічного allowlist; сесія валідується на не-прострочення та `EmailVerified`.

Залишкові ризики — це переважно **timing side-channels анти-енумерації** (AUTH-001/002/003), **захист у глибину** (відкритий CORS, відсутні security-заголовки, токен у localStorage, відсутній rate-limiting) та **дрейф документації vs коду** (задокументований HMAC-pepper не підключений). Усе це — реальні, але помірні зауваження, доречні для хакатону. Найвагоміше до production: **звузити CORS** і **додати security-заголовки + CSP**.

Спростоване хибне спрацювання (SCT-002) стверджувало «тихе скидання SMTP-облікових даних»; емпірична перевірка на тій самій версії фреймворку показала, що bind порожньої секції — це no-op, облікові дані зберігаються. Реального дефекту немає.

---

## 2. Підтверджені знахідки (CONFIRMED / PARTIAL)

Відсортовано за фінальною severity (Medium → Low → Info).

### Medium

| ID | Area | Severity | Файл:рядок | Impact | Рекомендація з виправлення |
|----|------|----------|------------|--------|----------------------------|
| **AUTH-001** | Authentication / анти-енумерація | Medium | `backend/src/TicketTracker.Application/Services/AuthService.cs:111` | `\|\|` короткозамикає: для невідомого email Argon2id `Verify` (m=19456, t=2, ~100-250 мс) не виконується, тоді як відомий email завжди платить цю ціну. Timing-оракул дозволяє відрізнити зареєстрований email від незареєстрованого, нівелюючи анти-енумерацію на рівні латентності (тіло відповіді коректно однакове). | Завжди виконувати constant-cost верифікацію: коли `user is null`, прогнати `Verify` проти фіксованого dummy-PHC-хешу й відкинути результат, щоб обидві гілки витрачали однаковий час. Поєднати з rate-limiting (AUTH-005). |
| **AUTH-002** | Email verification / анти-енумерація | Medium | `backend/src/TicketTracker.Application/Services/AuthService.cs:74` | Синхронний SMTP round-trip (ConnectAsync/AuthenticateAsync/SendAsync inline) **+ повільний Argon2id-хеш** виконуються лише для нового email; існуючий повертає той самий 201 миттєво без запису в БД і без SMTP. Вимірна енумерація акаунтів за латентністю на публічному непридушеному ендпоінті. | Винести надсилання листа з request-path: ставити в чергу / fire-and-forget **після** коміту відповіді, щоб новий і існуючий email поверталися за однаковий час. Не `await`-ити SMTP у запиті. |
| **SCT-001** | Transport / CORS | Medium | `backend/src/TicketTracker.Api/Program.cs:70` (застосовано на :81) | `AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()` зареєстровано безумовно та активно навіть при `ASPNETCORE_ENVIRONMENT=Production` (docker-compose.yml:60), що прямо суперечить задокументованій моделі «single-origin via nginx, no CORS» (ADR-0001 §24). Класичний CSRF не застосовний (auth — Bearer-заголовок, не cookie), тож реальний вплив — розширення поверхні атаки / відхід від моделі, а не пряма крадіжка токена. | Не викликати `UseCors` у Production **або** обмежити політику до відомого `FRONTEND_URL` origin з конкретними методами/заголовками. Загейтити пермісивну політику за `app.Environment.IsDevelopment()`. |

### Low

| ID | Area | Severity | Файл:рядок | Impact | Рекомендація з виправлення |
|----|------|----------|------------|--------|----------------------------|
| **AUTH-003** | Email verification / анти-енумерація | Low | `backend/src/TicketTracker.Application/Services/AuthService.cs:202` | Ранній `return nonCommittal` для невідомих/верифікованих email пропускає транзакцію + інвалідацію токенів + синхронний SMTP, які платить лише валідний-неверифікований акаунт. Timing-оракул для вузької підмножини «зареєстрований-АЛЕ-неверифікований». Тіло відповіді постійне. | Винести надсилання листа off request-path (background) і уникати timing-розбіжних ранніх return. Додати rate-limiting per email/IP. |
| **AUTH-004** | Зберігання токенів сесій/верифікації | Low | `backend/src/TicketTracker.Infrastructure/Security/CryptoTokenGenerator.cs:24` | Несолений SHA-256 замість задокументованого HMAC-pepper (`AUTH_TOKEN_SECRET`), який ніде не підключено в DI. Функціонально безпечно (pre-image — 256-бітний CSPRNG-токен), але втрачено шар defense-in-depth і є розбіжність код vs документація. | Або (а) реалізувати HMAC-SHA256 з ключем `AUTH_TOKEN_SECRET` із env і підключити в DI, або (б) прибрати згадку pepper з ARCHITECTURE §8. Бажано (а). |
| **AUTH-005** | Authentication / стійкість до зловживань | Low | `backend/src/TicketTracker.Application/Services/AuthService.cs:102` | Немає лічильника спроб / throttle / lockout на login та resend; жодного rate-limiting middleware. Необмежений онлайн-перебір пароля проти відомого акаунта + email-bombing через resend. Проєкт явно скоупить rate-limiting як «recommended (A32)». | Додати легкий per-IP та per-account rate-limiting (ASP.NET `RateLimiter` або sliding-window) на `/api/auth/login` та `/api/auth/resend-verification` + експоненційний backoff/тимчасовий lockout після повторних невдач. |
| **AUTH-006** | Token exposure / CORS | Low | `backend/src/TicketTracker.Api/Program.cs:73` | Той самий відкритий CORS, що й SCT-001, з кута експозиції токена. Header-based auth нейтралізує класичний CSRF; залишковий ризик — крос-origin читабельність API JSON будь-яким викликачем, що вже має валідний токен, + ослаблений defense-in-depth, що повністю покладається на (незабезпечений кодом) nginx. | Обмежити CORS відомим `FRONTEND_URL` origin; пермісивну політику — лише в Development. (Той самий фікс, що SCT-001.) |
| **INJ-01** | SMTP / HTML-ін'єкція (PARTIAL) | Low | `backend/src/TicketTracker.Infrastructure/Email/SmtpEmailSender.cs:42` | `verificationLink` інтерполюється в `href` без HTML/attribute-кодування. **Живого експлойту немає**: baseUrl — operator-controlled `FRONTEND_URL`, токен — `Uri.EscapeDataString` (не може містити `"`,`<`,`>`). Дефект інертний, суто defense-in-depth (спрацював би лише при misconfiguration FRONTEND_URL або майбутній зміні з user-input). Subject/TextBody безпечні (немає CRLF/header-injection). | HTML-кодувати лінк перед інтерполяцією (`WebUtility.HtmlEncode`) або будувати тіло через шаблонізатор, що кодує за замовчуванням. Опційно — валідувати `FRONTEND_URL` на старті як абсолютний http/https URI. |
| **SCT-003** | Transport / security-заголовки | Low | `frontend/nginx.conf:8` | nginx не віддає жодного security-заголовка (немає CSP, X-Content-Type-Options, X-Frame-Options, Referrer-Policy); index.html теж без CSP-meta. Відсутність CSP підсилює гіпотетичний XSS (токен у localStorage, SCT-004). Конкретного XSS-sink не продемонстровано (React екранує за замовчуванням) — суто missing-hardening. | Додати в nginx.conf: `Content-Security-Policy` (мінімум `default-src 'self'; connect-src 'self'; frame-ancestors 'none'`), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`. |
| **SCT-004** | Transport / зберігання токена | Low | `frontend/src/api/tokenStore.ts:24` | Сирий токен сесії пишеться в `localStorage` (`tt.auth.token`), читабельний будь-яким JS в origin; чинний до SESSION_TTL (72 год) або logout. Підсилено відсутністю CSP. Токен коректно ніколи не в URL (лише Authorization-заголовок). Це задокументований, прийнятий компроміс (ADR-0001 §34) із серверною ревокацією на logout. | Як мінімум — поставити CSP із SCT-003 як компенсуючий контроль. Для сильнішої постави — перейти на httpOnly+SameSite cookie-транспорт, який ADR-0001 описує як drop-in. |
| **SCT-005** | Secrets / крипто-конфігурація | Low | `backend/src/TicketTracker.Infrastructure/Security/CryptoTokenGenerator.cs:21` | `AUTH_TOKEN_SECRET` задокументований як «HMAC pepper» (README:113, ARCHITECTURE:384) і оператору сказано його встановити, але він **ніде не читається** — токени хешуються несоленим SHA-256. Внутрішній конфлікт документації (ADR-0001 §24/30/36 vs ARCHITECTURE §8). Не вразливість — оманлива конфігурація. | Або реалізувати HMAC-SHA256 з ключем `AUTH_TOKEN_SECRET` і підключити в DI, або прибрати секрет з .env.example/README/ARCHITECTURE і зафіксувати, що високоентропійні opaque-токени використовують несолений SHA-256 (як в ADR-0001). |
| **SCT-006** | Configuration / Transport | Low | `backend/src/TicketTracker.Api/appsettings.json:9` | `"AllowedHosts": "*"` вимикає валідацію Host-заголовка. Латентний ризик: API не публікує порт у docker-compose і доступний лише через nginx (`Host $host`), а `BuildVerificationLink` бере `FRONTEND_URL`, не Host — тож вектор наразі закритий. Спрацювало б лише при прямій публікації API. | Встановити `AllowedHosts` у конкретний внутрішній hostname / публічний домен замість `*` (defense-in-depth). |

### Info (за дизайном / спостереження)

| ID | Area | Severity | Файл:рядок | Impact | Рекомендація |
|----|------|----------|------------|--------|--------------|
| **AUTH-007** | Authentication / анти-енумерація | Info | `backend/src/TicketTracker.Application/Services/AuthService.cs:116` | `403 account_not_verified` повертається лише **після** успішної перевірки пароля → підтверджує існування акаунта ТА коректність пароля (на відміну від загального 401). Задокументований і прийнятий виняток (ADR-0001 A4, «єдиний scoped виняток енумерації»). Вузький: спрацьовує лише коли атакувальник вже подав валідні креденшели. | Код-змін не потрібно, якщо A4-компроміс стоїть. За бажання звузити пізніше — повертати загальний 401, а resend виносити в окремий автентифікований flow. |
| **AUTH-008** | Хешування паролів | Info | `backend/src/TicketTracker.Infrastructure/Security/Argon2PasswordHasher.cs:17` | Argon2id m=19456/t=2/p=1 — найнижчий із двох базових профілів OWASP (у межах гайдансу, не нижче). CSPRNG-сіль на хеш + constant-time verify коректні. Залишок — обмежений запас проти майбутнього offline GPU/ASIC-крекінгу за умови ексфільтрації хешів. | Дій не треба для поточного скоупу. Для production — пере-тюнінг до вищого профілю (46-64 MiB) після заміру + rehash-on-login при зміні параметрів (PHC-формат це підтримує). |
| **SCT-007** | Authorization / контроль доступу | Info | `backend/src/TicketTracker.Application/Services/TicketService.cs:42` | Будь-який верифікований користувач може читати/змінювати всі teams/epics/tickets/comments за id — немає membership-предиката. Це **не IDOR** за поточним контрактом: membership поза скоупом, ARCHITECTURE/API_CONTRACT визначають єдиний спільний workspace, де authorization == «верифікований залогінений користувач». Шлюз auth коректний. | Код-змін не треба для поточного скоупу. Якщо колись вводиться multi-tenant/per-team ізоляція — додати membership-предикат до кожного запиту й by-id fetch; не покладатися на неугадуваність id. |

---

## 3. Спростовані знахідки (REFUTED — хибні спрацювання)

| ID | Заявлена severity | Фінал | Чому відхилено |
|----|-------------------|-------|----------------|
| **SCT-002** | High | Info (no defect) | Заявлено «тихе скидання SMTP-облікових даних»: нібито `services.Configure<SmtpOptions>(GetSection("Smtp"))` (InfrastructureServiceCollectionExtensions.cs:22) над неіснуючою секцією `Smtp` виконується останнім і скидає Username/Password/Host до class-defaults, через що `AuthenticateAsync` пропускається. **Несуча причинна теза хибна**: `IConfiguration.Bind` над порожньою секцією — це **no-op**; `ConfigurationBinder` присвоює властивість лише за наявності відповідного ключа й ніколи не записує class-defaults назад. Емпірично перевірено на тій самій версії фреймворку (net10.0, Microsoft.Extensions.Options.ConfigurationExtensions 10.0.9) з відтворенням точного порядку реєстрації: Host/Username/Password з env **повністю збережені**, `AuthenticateAsync` НЕ пропускається. Рядок 22 — лише надлишковий/vestigial no-op (косметика коду), не дефект безпеки. |

---

## 4. Швидкі перемоги (зробити перед демо)

Пріоритезовано за співвідношенням «вплив / зусилля». Усі — невеликі, локалізовані зміни.

1. **Звузити CORS до `FRONTEND_URL`** (SCT-001 / AUTH-006) — найвищий пріоритет. Прибрати `AllowAnyOrigin` з default/production-шляху; пермісивну політику лишити лише під `IsDevelopment()`. Один фікс закриває дві Medium-знахідки та усуває найпомітніший відхід від задокументованої моделі безпеки.
2. **Додати security-заголовки в nginx** (SCT-003) — CSP (`default-src 'self'; connect-src 'self'; frame-ancestors 'none'`), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`. Кілька рядків у `nginx.conf`; одночасно є компенсуючим контролем для токена в localStorage (SCT-004).
3. **Усунути дрейф документації по `AUTH_TOKEN_SECRET`** (AUTH-004 / SCT-005) — або підключити HMAC-pepper у DI, або прибрати pepper-формулювання з README/ARCHITECTURE. Найдешевший варіант (б) — лише правки документації, ставить код і дизайн у згоду, прибирає оманливий крок налаштування для оператора.
4. **Прибрати vestigial `Configure<SmtpOptions>(GetSection("Smtp"))`** (InfrastructureServiceCollectionExtensions.cs:22) — не дефект (див. SCT-002), але мертвий no-op, що ввів в оману рев'ю; видалення прибирає майбутню пастку.
5. _(Опційно, якщо лишається час)_ **Equal-cost анти-енумерація** (AUTH-001) — dummy-Argon2id-verify на null-user гілці. Невелика зміна, закриває найчіткіший timing-оракул.

## 5. Прийнятно поза скоупом

Свідомі, задокументовані рішення або production-hardening, які **не варто** виправляти в рамках хакатону:

- **RBAC / membership-ізоляція** (SCT-007) — єдиний спільний workspace є задокументованою властивістю дизайну (ARCHITECTURE / API_CONTRACT), не дефектом. Шлюз автентифікації коректний.
- **`account_not_verified` як scoped виняток енумерації** (AUTH-007) — усвідомлений UX-компроміс A4, спрацьовує лише після коректних креденшелів.
- **Параметри Argon2id на базовому профілі OWASP** (AUTH-008) — у межах гайдансу, доречно для хакатон-масштабу (~100-250 мс); пере-тюнінг лишити на production.
- **Токен у localStorage** (SCT-004) — задокументований компроміс ADR-0001 §34 із серверною ревокацією; httpOnly-cookie описаний як drop-in на майбутнє.
- **Rate-limiting / lockout** (AUTH-005, частково AUTH-002/003) — явно «recommended, not mandated (A32)» у REQUIREMENTS_ANALYSIS / API_CONTRACT / ARCHITECTURE. Production-grade async-розсилка пошти й abuse-контролі поза скоупом.
- **Timing side-channels анти-енумерації** (AUTH-002/003) — стандартний фікс (background-черга пошти) — це production-mail-hardening, поза скоупом; на рівні тіла відповіді анти-енумерація вже коректна.
- **`AllowedHosts: "*"`** (SCT-006) — латентний; реальний лише при прямій публікації API, чого топологія docker-compose не робить.
- **Несолений SHA-256 для opaque-токенів** (AUTH-004 крипто-аспект) — криптографічно прийнятно для 256-бітних CSPRNG-входів; до фіксу веде лише розбіжність із документацією, не сама криптографія.
