# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Jiten is a free, open-source platform for Japanese immersion learners that analyzes Japanese media to provide detailed statistics (character count, difficulty, vocabulary lists, frequency lists, etc.) and generates Anki decks. See https://jiten.moe for the live platform.

## Repository Structure

```
/
├── Jiten.Api/            # ASP.NET Core Web API
├── Jiten.Cli/            # Command-line tool for batch processing
├── Jiten.Core/           # Core library with domain models and data access
├── Jiten.Parser/         # Japanese text parsing engine
├── Jiten.Tests/          # xUnit test suite
├── Jiten.Web/            # Nuxt 3 frontend application
├── Shared/               # Shared resources (dictionaries, models, etc.)
└── Jiten.sln             # Main .NET solution file
```

## Full Stack Development Workflow (Windows)

To run the full application locally, you will need to start both the backend API and the frontend development server.

1.  **Start the Backend API:**
    Open a terminal and run the following command:
    ```bash
    dotnet run --project Jiten.Api/Jiten.Api.csproj
    ```
    The API will be available at `https://localhost:7299`.

2.  **Start the Frontend Application:**
    Open a **new** terminal and navigate to the `Jiten.Web` directory. Then run:
    ```bash
    cd Jiten.Web
    pnpm install
    pnpm dev
    ```
    The frontend will be available at `https://localhost:3000`.

## Build & Run Commands (Windows)

### Backend (.NET)

Use these commands from the root directory.

```bash
# Build the entire .NET solution
dotnet build Jiten.sln

# Build a specific project (e.g., Jiten.Api)
dotnet build Jiten.Api/Jiten.Api.csproj

# Run the backend API locally
dotnet run --project Jiten.Api/Jiten.Api.csproj

# Run the command-line tool
dotnet run --project Jiten.Cli/Jiten.Cli.csproj -- [options]

# Run tests
dotnet test Jiten.Tests/Jiten.Tests.csproj
```

## Solution Architecture

The solution consists of 5 projects targeting .NET 9.0 and a Nuxt frontend:

### Jiten.Core
Core library containing:
- **Domain Models**: `Deck`, `DeckWord`, `JmDictWord`, `ExampleSentence`, etc.
- **Data Access**: `JitenDbContext` (main data) and `UserDbContext` (authentication)
- **Database**: PostgreSQL with Entity Framework Core
- **Extractors**: `EbookExtractor` for epub/pdf files
- **ML Models**: `DifficultyPredictor` using ONNX models
- **Utilities**: BunnyCDN integration, metadata providers (Anilist, VNDB, Google Books, IGDB)

### Jiten.Parser
Japanese text parsing engine:
- **MorphologicalAnalyser**: Tokenizes Japanese text using Sudachi (via native interop)
- **Deconjugator**: Reverses Japanese verb/adjective conjugations using rule-based system (deconjugator.json)
- **Parser**: Main parsing pipeline that coordinates morphological analysis → deconjugation → JMDict lookup
- **Caching**: Redis-backed caching (`RedisDeckWordCache`, `RedisJmDictCache`) for performance
- **ExampleSentenceExtractor**: Extracts example sentences from parsed text

**Key Flow**:
1. Text → MorphologicalAnalyser (Sudachi) → WordInfo objects with POS tags
2. WordInfo → Deconjugator (if verb/adjective) → base forms
3. Base forms → JMDict lookup (via lookups table) → JmDictWord matches
4. Results → DeckWord objects with occurrence counts

### Jiten.Cli
Command-line tool for batch processing:
- **Extractors**: Multiple format-specific extractors for Japanese media:
  - `BruteforceExtractor`: Regex-based extraction from any file (fallback option)
  - Visual novel engines: `KiriKiriExtractor`, `BgiExtractor`, `YuRisExtractor`, `PsbExtractor`, etc.
  - `TxtExtractor`: Plain text files
  - `MokuroExtractor`: Manga OCR files
  - Subtitles handled via `SubtitlesParser` library
- **Commands**: Parse directories, extract text, download metadata, import dictionaries
- **Shared Resources**: Copies files from `../Shared` folder during build

### Jiten.Api
ASP.NET Core Web API:
- **Controllers**: Media decks, vocabulary, user management, authentication, admin operations
- **Background Jobs**: Hangfire for async processing (`ParseJob`, `ComputationJob`, `FetchMetadataJob`, `ReparseJob`)
- **Authentication**:
  - JWT Bearer tokens (`TokenService`)
  - API Keys (`ApiKeyAuthenticationHandler`)
  - Google OAuth
  - ASP.NET Core Identity with 2FA support
- **Services**: `EmailService`, `CurrentUserService`, `ApiKeyService`
- **Swagger**: OpenAPI documentation at `/swagger`

### Jiten.Tests
xUnit test suite using FluentAssertions:
- `DeconjugatorTests`: Verb/adjective deconjugation
- `MorphologicalAnalyserTests`: Sudachi integration
- `FsrsTests`: Spaced repetition algorithm

### Frontend Application (Jiten.Web)

The frontend is a Nuxt 3 application that provides a modern, responsive user interface.

*   **Technology Stack:** Built with Nuxt 3, Vue 3, TypeScript, and Pinia for state management.
*   **UI:** Uses PrimeVue for components and TailwindCSS for styling.
*   **API Communication:** Interacts with the `Jiten.Api` backend via a custom `useApiFetch` composable that handles JWT authentication and token refreshing. The base API URL is configured in `nuxt.config.ts`.
*   **Key Features:**
    *   Media browsing and detailed statistics.
    *   Vocabulary lists with pitch accent visualization.
    *   User accounts with vocabulary tracking.
    *   AnkiConnect integration for deck exporting.
    *   Admin dashboard for content management.
*   **Routing:** Nuxt's file-based routing is used to create pages for media decks, vocabulary, user settings, and more. Middleware is used to protect authenticated and admin-only routes.

## Database Architecture

### JitenDbContext (schema: jiten)
- **Decks**: Media entries with statistics (character count, difficulty, word count)
  - Parent-child relationships for multi-volume works
  - Links to external sites (Anilist, VNDB, etc.)
- **DeckWords**: Word occurrences in decks (WordId + ReadingIndex + Occurrences)
- **DeckRawText**: Optional raw text storage
- **ExampleSentences**: Sentences extracted from decks with word references

### JitenDbContext (schema: jmdict)
- **Words**: JMDict dictionary entries (WordId, Readings[], PartsOfSpeech[], PitchAccents[])
- **Definitions**: Multilingual definitions (English, French, German, Spanish, etc.)
- **Lookups**: Fast lookup table mapping text → WordIds
- **WordFrequencies**: Global frequency statistics

### UserDbContext (default schema)
- ASP.NET Core Identity tables
- **UserCoverage**: Known vocabulary tracking
- **UserMetadata**: User preferences
- **ApiKeys**: API authentication
- **RefreshTokens**: JWT refresh tokens

### Important Indexes
- PGroonga full-text search on Decks titles (must be manually created, see README)
- WordId + ReadingIndex composite indexes for fast lookups
- DeckId indexes for query optimization

## Japanese Text Processing Pipeline

### Word Deconjugation & Lookup Process

1. **Morphological Analysis** (Jiten.Parser/MorphologicalAnalyser.cs):
   - Sudachi native library tokenizes text
   - Produces WordInfo objects with: Text, DictionaryForm, PartOfSpeech, NormalizedForm

2. **Deconjugation** (Jiten.Parser/Deconjugator.cs):
   - For verbs/adjectives: applies deconjugation rules from `deconjugator.json`
   - Rule types: stdrule, rewriterule, onlyfinalrule, neverfinalrule, contextrule, substitution
   - Returns possible base forms with conjugation history
   - Uses object pooling and caching for performance

3. **JMDict Lookup** (Jiten.Parser/Parser.cs):
   - Queries Lookups table for text/hiragana matches
   - Retrieves JmDictWord candidates from Redis cache (or DB)
   - Matches based on PartOfSpeech compatibility
   - Priority scoring for ambiguous matches (prefers non-kana readings for kanji input)
   - Falls back to alternative POS if no match found

4. **Result** (Jiten.Core/Data/DeckWord.cs):
   - WordId: JMDict entry ID
   - ReadingIndex: Which reading variant matched (0-255)
   - OriginalText: Surface form from text
   - Conjugations: Deconjugation steps applied
   - Occurrences: Frequency count

### Performance Optimization

**Parser Cache Settings** (Jiten.Parser/Parser.cs:21-23):
- Word cache: Stores DeckWord results by (Text, POS, DictionaryForm)
- JMDict cache: Redis-backed cache of all dictionary entries
- See README for performance benchmarks (8M moji/min with cache)

**Deconjugator Cache** (Jiten.Parser/Deconjugator.cs:19-22):
- Optional in-memory cache for deconjugation results
- Object pooling for List/HashSet to reduce allocations
- Pre-cached virtual rules for multi-variant rules

## Extractor System

Extractors implement a common pattern:
```csharp
public async Task<string> Extract(string? filePath, string encoding, bool verbose)
```

### BruteforceExtractor (Jiten.Cli/Extractors/BruteforceExtractor.cs)
- Uses regex to match Japanese character ranges (Hiragana, Katakana, CJK Ideographs)
- Processes files or entire directories recursively
- Supports configurable encoding (default: Shift-JIS for Japanese games)
- Fallback option when format-specific extractors fail

### Visual Novel Extractors
- Engine-specific extraction logic for encrypted/proprietary formats
- Common engines: KiriKiri, BGI, YuRis, PSB, MSC, MES, Nexas, Whale, UTF, CS2

## Shared Resources Folder

The `Shared/` directory contains resources copied to output directories during build:

### Sudachi Dictionary Files
- `sudachi.json`, `sudachi_nouserdic.json`: Morphological analyzer configuration
- `char.def`, `unk.def`, `rewrite.def`: Sudachi dictionaries
- `sudachi_lib.dll` (Windows), `libsudachi_lib.so` (Linux): Native binaries
- `user_dic.dic`, `user_dic.zip`: Custom user dictionary

### Deconjugation
- `deconjugator.json`: Verb/adjective conjugation rules (from JL/Nazeka)

### Machine Learning
- `difficulty_prediction_model_novels.onnx`: ONNX model for novels/VNs/games
- `difficulty_prediction_model_shows.onnx`: ONNX model for anime/dramas

### Anki
- `lapis.apkg`: Base Anki note type template

### Configuration
- `sharedsettings.json`: Shared configuration (DB connection strings, Redis, etc.)

## Configuration Files

All projects use layered configuration:
1. `Shared/sharedsettings.json` (or `../Shared/sharedsettings.json`)
2. `appsettings.json`
3. `appsettings.{Environment}.json`
4. Environment variables

Required settings (see `.env.example` in README):
- PostgreSQL connection string
- Redis connection string
- JWT secret (32+ chars for HS256)
- Email SMTP settings
- BunnyCDN credentials (for file storage)
- OAuth client IDs (Google)

## Testing

- Framework: xUnit with FluentAssertions
- Run single test: `dotnet test --filter "FullyQualifiedName~DeconjugatorTests.DeconjugationTest"`
- Tests require Shared resources to be present

## Important Architectural Notes

### Dependency Flow
- Jiten.Api depends on: Jiten.Core, Jiten.Parser
- Jiten.Cli depends on: Jiten.Core, Jiten.Parser
- Jiten.Parser depends on: Jiten.Core (for data models)
- Jiten.Tests depends on: Jiten.Parser

### Redis Caching Strategy
- Cache initialization is lazy and thread-safe (semaphore-based)
- JMDict cache is populated in batches (10K words) if empty
- Cache keys use (Text, POS, DictionaryForm) tuples for DeckWords
- Cache failures are non-fatal (logs warning, continues without cache)

### Difficulty Prediction
- Uses ONNX Runtime with pre-trained LightGBM models
- Features extracted from: word frequencies, kanji counts, sentence length, dialogue percentage
- Separate models for different media types (novels vs shows)
- See Jiten.Core/ML/DifficultyPredictor.cs

### Hangfire Background Jobs
- Persistent job queue using PostgreSQL storage
- Jobs: ParseJob (text parsing), ComputationJob (statistics), FetchMetadataJob (external APIs)
- Dashboard at `/hangfire` (restricted to admins)

### Entity Framework Migrations
- Migrations in: Jiten.Core/Migrations (JitenDbContext), Jiten.Core/Migrations/UserDb (UserDbContext)
- Apply migrations: `dotnet ef database update --project Jiten.Core --startup-project Jiten.Core`
- Add migration: `dotnet ef migrations add MigrationName --project Jiten.Core --startup-project Jiten.Core --context JitenDbContext`

## Jiten.Web - Frontend Application

### Technology Stack

Jiten.Web is a Nuxt 3 frontend application that provides the user interface for jiten.moe.

**Core Framework:**
- Nuxt 3 (v3.19+) - Vue 3 meta-framework with SSR/SSG support
- Vue 3 - Latest Vue with Composition API
- TypeScript - Full type safety
- Pinia - State management (stores)

**UI Framework & Styling:**
- PrimeVue 4 - Component library with custom Jiten theme (purple primary color)
- TailwindCSS 4 - Utility-first CSS framework
- Tailwindcss-PrimeUI - Integration between Tailwind and PrimeVue
- Noto Sans JP - Google Font for Japanese text
- Dark mode support via CSS class `.dark-mode`

**Key Dependencies:**
- `@nuxt/icon` - Icon system
- `@nuxtjs/seo` - SEO utilities and sitemap generation
- `nuxt-umami` - Analytics tracking
- `nuxt-vue3-google-signin` - Google OAuth integration
- `vue-recaptcha` - reCAPTCHA v2 integration
- `hatsuon` - Japanese pitch accent visualization
- `yanki-connect` - AnkiConnect API integration for importing vocabulary
- `chart.js` + `vue-chartjs` - Data visualization
- `perfect-debounce` - Input debouncing utilities

### Directory Structure

```
Jiten.Web/
├── assets/
│   ├── css/main.css           # Global styles, Tailwind imports
│   └── img/                   # Static images
├── components/                # Vue components
│   ├── AppHeader.vue          # Site navigation header
│   ├── AppFooter.vue          # Site footer
│   ├── MediaDeckCard.vue      # Media deck display card
│   ├── MediaDeckTableView.vue # Table view for media decks
│   ├── MediaDeckCompactView.vue # Compact view for media decks
│   ├── VocabularyEntry.vue    # Word entry display
│   ├── VocabularyDefinitions.vue # Word definitions
│   ├── VocabularyDetail.vue   # Detailed word view
│   ├── WordSearch.vue         # Word search interface
│   ├── PitchDiagram.vue       # Pitch accent visualization
│   ├── DifficultyDisplay.vue  # Difficulty rating display
│   ├── AnkiConnectImport.vue  # AnkiConnect vocabulary import
│   ├── SettingsCoverage.vue   # User vocabulary coverage settings
│   └── ...                    # More components
├── composables/
│   ├── useApiFetch.ts         # API fetch wrapper with auth
│   └── useJpdbApi.ts          # JPDB integration
├── middleware/
│   ├── auth.ts                # Authentication middleware
│   └── authAdmin.ts           # Admin-only route protection
├── pages/                     # File-based routing
│   ├── index.vue              # Home page
│   ├── login.vue              # Login page
│   ├── register.vue           # Registration page
│   ├── settings.vue           # User settings
│   ├── parse.vue              # Text parsing interface
│   ├── parse-deck.vue         # Deck parsing interface
│   ├── decks/media/
│   │   ├── index.vue          # Media deck browser
│   │   ├── list/[mediaType].vue # Filtered by media type
│   │   └── [id]/
│   │       ├── detail.vue     # Deck details
│   │       └── vocabulary.vue # Deck vocabulary list
│   ├── vocabulary/[wordId]/[readingIndex].vue # Word detail page
│   └── dashboard/             # Admin dashboard
│       ├── index.vue          # Admin home
│       ├── add-media.vue      # Add new media
│       ├── manage.vue         # Manage media
│       └── media/[id].vue     # Edit media
├── plugins/
│   └── api.ts                 # Global $api plugin with auto-refresh
├── public/                    # Static assets (favicon, robots.txt)
├── server/
│   └── api/__sitemap__/urls.ts # Dynamic sitemap generation
├── stores/
│   ├── authStore.ts           # Authentication state (JWT, user, login/logout)
│   ├── jitenStore.ts          # App settings (theme, language, preferences)
│   └── displayStyleStore.ts   # Display preferences
├── types/
│   ├── types.ts               # TypeScript interfaces (Deck, Word, etc.)
│   └── enums.ts               # Enum definitions
├── utils/                     # Helper functions
│   ├── mediaTypeMapper.ts     # MediaType enum mapping
│   ├── deckFormatMapper.ts    # Deck format conversions
│   ├── convertToRuby.ts       # Furigana conversion
│   ├── stripRuby.ts           # Remove ruby annotations
│   └── ...                    # More utilities
├── app.vue                    # Root component
├── nuxt.config.ts             # Nuxt configuration
├── package.json               # Dependencies and scripts
├── tsconfig.json              # TypeScript config
├── Dockerfile                 # Production build container
└── localhost.pem/key.pem      # Local HTTPS certificates
```

### Build & Development Commands

```bash
# Install dependencies (uses pnpm)
pnpm install

# Development server with HTTPS (https://localhost:3000)
pnpm dev

# Development server accessible from mobile devices
pnpm dev-mobile

# Production build
pnpm build

# Preview production build
pnpm preview

# Lint code (ESLint + Prettier)
pnpm lint

# Auto-fix linting issues
pnpm lintfix
```

**Note:** Development uses HTTPS with self-signed certificates (`localhost.pem`, `localhost-key.pem`) for Google Sign-In testing.

### API Integration

**Backend Connection:**
- Base API URL configured in `nuxt.config.ts`: `https://localhost:7299/api/` (dev)
- Production URL injected via environment variable `NUXT_PUBLIC_BASE_URL`

**API Client Architecture:**

1. **useApiFetch composable** (composables/useApiFetch.ts):
   - Wrapper around Nuxt's `useFetch` with automatic JWT bearer token injection
   - Supports standard and paginated responses (`PaginatedResponse<T>`)
   - Request deduplication via unique keys

2. **$api plugin** (plugins/api.ts):
   - Global `$fetch` wrapper available as `$api` throughout the app
   - Automatic token refresh on 401 responses
   - Prevents infinite loops on auth endpoints
   - Auto-redirects to login on auth failures

**Authentication Flow:**
- JWT access tokens stored in cookies (7-day expiry)
- Refresh tokens stored in cookies (30-day expiry)
- Automatic token refresh before expiry (5-minute window)
- Auth middleware protects routes requiring authentication
- Supports Google OAuth and traditional email/password

### State Management (Pinia Stores)

**authStore** (stores/authStore.ts):
- User authentication state (access token, refresh token, user data)
- Login/logout actions (email/password, Google OAuth)
- Automatic token refresh logic
- Computed: `isAuthenticated`, `isAdmin`

**jitenStore** (stores/jitenStore.ts):
- User preferences persisted in cookies (1-year expiry):
  - Title language (Romaji/English/Original)
  - Display furigana toggle
  - Dark mode toggle
  - Reading speed (characters/minute)
  - NSFW content visibility
  - Vocabulary definition visibility
  - Difficulty display style
- Known word IDs stored in localStorage
- Preferences synced across browser sessions

**displayStyleStore** (stores/displayStyleStore.ts):
- UI display preferences for media deck views

### Routing & Pages

Nuxt uses file-based routing. Key routes:

- `/` - Home page with media search
- `/decks/media` - Browse all media decks
- `/decks/media/list/[mediaType]` - Filter by media type (novel, anime, VN, etc.)
- `/decks/media/[id]/detail` - Media deck details and statistics
- `/decks/media/[id]/vocabulary` - Full vocabulary list for a deck
- `/vocabulary/[wordId]/[readingIndex]` - Word detail page with definitions, pitch accent, example sentences
- `/parse` - Parse custom Japanese text
- `/settings` - User settings (coverage, vocabulary import)
- `/dashboard/*` - Admin pages (requires Administrator role)

**Middleware:**
- `auth.ts` - Requires valid JWT, refreshes if expired
- `authAdmin.ts` - Requires Administrator role

### TypeScript Types

Types mirror the C# backend models (types/types.ts):

**Core Interfaces:**
- `Deck` - Media deck with statistics (character count, difficulty, coverage, etc.)
- `DeckWord` - Word occurrence in a deck
- `Word` - JMDict word with readings, definitions, pitch accents
- `Reading` - Word reading variant (kana/kanji) with frequency data
- `Definition` - Word meanings by part of speech
- `Link` - External links (Anilist, VNDB, etc.)
- `ExampleSentence` - Example sentence from a deck
- `PaginatedResponse<T>` - Paginated API responses

**Enums:**
- `MediaType` - Novel, Anime, VisualNovel, Manga, etc.
- `ReadingType` - Kana, KanjiWithKanaReading, etc.
- `KnownState` - Unknown, Known, Learning
- `TitleLanguage` - Original, Romaji, English

### Configuration & Environment Variables

**Runtime Config** (nuxt.config.ts):
- `baseURL`: API endpoint (default: https://localhost:7299/api/)
- `recaptcha.v2SiteKey`: reCAPTCHA site key

**Build-time Environment Variables** (Dockerfile):
- `NUXT_PUBLIC_BASE_URL` - Production API endpoint
- `NUXT_PUBLIC_GOOGLE_SIGNIN_CLIENT_ID` - Google OAuth client ID
- `NUXT_PUBLIC_RECAPTCHA_V2_SITE_KEY` - reCAPTCHA site key
- `NUXT_PUBLIC_SCRIPTS_UMAMI_ANALYTICS_WEBSITE_ID` - Umami analytics ID
- `NUXT_PUBLIC_SCRIPTS_UMAMI_ANALYTICS_HOST_URL` - Umami analytics host

### Docker Deployment

**Multi-stage build** (Dockerfile):
1. Build stage: Install deps, build Nuxt app (`npm run build`)
2. Final stage: Copy `.output/` directory, run Node server
3. Exposes port 3001
4. Entry point: `node server/index.mjs`

**Production Environment:**
- SSR mode (server-side rendering)
- Sitemap generated from API (`/api/__sitemap__/urls`)
- SEO meta tags via `@nuxtjs/seo`
- Analytics via Umami (privacy-focused)

### Important Architectural Patterns

**Composables:**
- `useApiFetch` for typed API calls with auth
- `useJpdbApi` for JPDB integration (external vocabulary service)

**Auto-imports:**
- Nuxt auto-imports Vue APIs, composables, components, and utils
- No explicit imports needed for most common utilities

**SEO:**
- Server-side rendering for crawlability
- Dynamic meta tags per page
- Sitemap auto-generated from backend deck list
- OG image generation with custom fonts

**Japanese Text Handling:**
- Furigana rendering via `<ruby>` tags
- Pitch accent diagrams using `hatsuon` library
- JMDict word lookup and display
- Coverage calculation based on user vocabulary

### Development Notes

1. **API Base URL**: Change `nuxt.config.ts` `baseURL` if backend runs on different port
2. **HTTPS Required**: Google Sign-In requires HTTPS even in dev (uses self-signed certs)
3. **TypeScript**: Nuxt generates `.nuxt/tsconfig.json` - extend it in root `tsconfig.json`
4. **Styling**: Use TailwindCSS utility classes + PrimeVue components for consistency
5. **Icons**: Use `@nuxt/icon` with `<Icon name="..." />` syntax or PrimeVue icons with pi-iconname
6. **Forms**: PrimeVue forms module (`@primevue/forms`) for validation
7. **State Persistence**: Use cookies for server-accessible state, localStorage for client-only data
8. **AnkiConnect**: Requires Anki desktop running with AnkiConnect plugin for vocabulary import
