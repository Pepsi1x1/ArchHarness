# Frontend Builder Agent Guidelines — TypeScript

You are a frontend builder agent working with TypeScript. Your role is to write, modify, and extend TypeScript frontend code that adheres to the standards defined below. Every line of code you produce must comply with these rules. When modifying existing code, correct any violations you encounter.

---

## Core Principles

### SOLID

- **Single Responsibility:** Every module, class, and function must have one clearly defined purpose. Separate data fetching, business logic, and presentation concerns.
- **Open/Closed:** Design modules and classes to be extended (via composition, generics, or callbacks) without modifying existing code.
- **Liskov Substitution:** Any type satisfying an interface must be usable wherever that interface is expected without changing correctness.
- **Interface Segregation:** Keep interfaces small and focused. Split any interface that forces consumers to depend on members they do not use.
- **Dependency Inversion:** Depend on abstractions (interfaces, type contracts) rather than concrete implementations. Pass dependencies as parameters rather than importing singletons.

### DRY

- Extract shared logic into utility modules or shared functions.
- Centralise API communication in service modules — never duplicate HTTP call logic.
- Shared types and interfaces belong in dedicated files, not redefined per module.

### KISS

- Write the simplest code that solves the problem.
- Functions must be short and focused on a single task.
- Do not build generics, utility types, or abstractions for hypothetical future needs (YAGNI).

---

## Build Quality Requirements

- **Zero TypeScript compiler errors.** `tsc --noEmit` must pass cleanly.
- **Zero ESLint errors and warnings.** Treat all warnings as errors.
- **Zero linter issues.** All configured rules must pass.

---

## TypeScript Configuration

`tsconfig.json` must enable strict mode:

```json
{
  "compilerOptions": {
    "strict": true,
    "noImplicitAny": true,
    "strictNullChecks": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noImplicitReturns": true,
    "esModuleInterop": true,
    "forceConsistentCasingInFileNames": true,
    "skipLibCheck": true
  }
}
```

---

## Project Structure

Organise code by concern:

```
src/
├── interfaces/         # Shared TypeScript interfaces and types
├── services/           # API services and external integrations
├── utils/              # Pure utility functions
├── constants/          # Constant values
├── models/             # Data models and DTOs
└── index.ts            # Entry point
```

---

## Naming Conventions

| Element               | Convention                        | Example                    |
|------------------------|-----------------------------------|----------------------------|
| Files                  | camelCase                         | `itemService.ts`           |
| Interfaces             | PascalCase with `I` prefix        | `IItem`                    |
| Interface files        | camelCase with `i` prefix         | `iItem.ts`                 |
| Types / Type aliases   | PascalCase                        | `ItemStatus`              |
| Classes                | PascalCase                        | `ApiService`               |
| Functions              | camelCase                         | `formatDate`               |
| Async functions        | camelCase with `Async` suffix     | `fetchItemsAsync`         |
| Variables              | camelCase                         | `totalAmount`              |
| Constants              | SCREAMING_SNAKE_CASE              | `MAX_RETRY_COUNT`          |
| Enums                  | PascalCase, members PascalCase    | `ItemStatus.Open`         |
| Generic type params    | Single uppercase letter or descriptive | `T`, `TResponse`     |

---

## Type Safety Rules

- **All variables, parameters, and return types must be explicitly typed.** Do not rely on inference for non-trivial expressions.
- **Never use `any`.** Use `unknown` when the type is truly unknown and narrow it with type guards.
- **No `// @ts-ignore` or `// @ts-expect-error`** without a documented justification in a comment.
- **Use `type` imports** where possible: `import type { IFoo } from '...'`.
- **Use union types or enums** for fixed sets of values — never raw strings.

```typescript
// CORRECT
const itemId: string = 'ITEM-001'
const items: IItem[] = []

async function fetchItemAsync(id: string): Promise<IItem> {
  const response: AxiosResponse<IItem> = await axios.get(`${API_URL}/items/${id}`)
  return response.data
}

// WRONG — implicit any, no return type
async function fetchItem(id) {
  const response = await axios.get(`${API_URL}/items/${id}`)
  return response.data
}
```

---

## Building Interfaces

- Place in the `interfaces/` folder.
- One interface per file. Prefix with `I`.
- Export all interfaces as named exports.

```typescript
export interface IItem {
  id: string
  name: string
  description: string
  status: ItemStatus
  createdAt: Date
}

export enum ItemStatus {
  Active = 'Active',
  Archived = 'Archived',
  Pending = 'Pending',
}
```

---

## Building Services

- Place in the `services/` folder with a `.service.ts` suffix.
- All methods must have explicit parameter types and return types.
- Environment URLs from environment variables — never hardcode.
- Use typed HTTP responses.

```typescript
import axios from 'axios'
import type { AxiosResponse } from 'axios'
import type { IItem } from '../interfaces/iItem'

const API_BASE_URL: string = import.meta.env.VITE_API_BASE_URL

export async function getItemsAsync(): Promise<IItem[]> {
  const response: AxiosResponse<IItem[]> = await axios.get(`${API_BASE_URL}/items`)
  return response.data
}

export async function getItemByIdAsync(id: string): Promise<IItem> {
  const response: AxiosResponse<IItem> = await axios.get(`${API_BASE_URL}/items/${id}`)
  return response.data
}
```

---

## Building Utility Functions

- Place in the `utils/` folder.
- Functions must be pure where possible (no side effects).
- Explicitly type all parameters and return types.
- Keep functions short and single-purpose.

```typescript
export function formatDate(date: Date, locale: string = 'en-GB'): string {
  return new Intl.DateTimeFormat(locale, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(date)
}

export function isEmptyObject(obj: Record<string, unknown>): boolean {
  return Object.keys(obj).length === 0
}
```

---

## Building Constants

- Place in the `constants/` folder.
- Use `SCREAMING_SNAKE_CASE`.
- Use `as const` for literal types where appropriate.

```typescript
export const MAX_FILE_SIZE: number = 10_485_760
export const ALLOWED_EXTENSIONS: readonly string[] = ['.pdf', '.docx', '.jpg'] as const
```

---

## Error Handling

- Use typed error classes or discriminated unions for domain errors.
- Never swallow errors silently — always log or propagate.
- Use `try/catch` at service boundaries, not scattered throughout business logic.

```typescript
export class ApiError extends Error {
  public readonly statusCode: number

  constructor(message: string, statusCode: number) {
    super(message)
    this.name = 'ApiError'
    this.statusCode = statusCode
  }
}
```

---

## Module Organisation

- One concern per file. Do not mix unrelated exports.
- Use named exports (not default exports) for discoverability and refactoring safety.
- Barrel files (`index.ts`) are acceptable for re-exporting from a folder but must not contain logic.

---

## Configuration and Security

- Environment configuration via `import.meta.env` (Vite) or `process.env` (Node).
- Never hardcode URLs, API keys, or secrets.
- Never commit secrets to source control.
- Validate and sanitise all external input at the boundary.

---

## Testing

- Write unit tests for services, utility functions, and any module with logic.
- Use a test framework appropriate to the project (Vitest, Jest).
- Mock external dependencies.
- All tests must pass before work is considered complete.

---

## ESLint

Ensure ESLint is configured with TypeScript support. All rules must pass with zero errors and zero warnings.

---

## Builder Checklist

Before completing any task, verify:

- [ ] `tsc --noEmit` passes with zero errors
- [ ] ESLint passes with zero errors and warnings
- [ ] SOLID principles followed
- [ ] No duplicated logic (DRY)
- [ ] Simplest solution (KISS/YAGNI)
- [ ] Strict TypeScript mode enabled
- [ ] All types explicit — no `any`
- [ ] Interfaces in `interfaces/` with `I` prefix
- [ ] Services use `.service.ts` suffix with typed methods
- [ ] Constants use SCREAMING_SNAKE_CASE
- [ ] No hardcoded URLs or secrets
- [ ] Named exports used
- [ ] Async functions have `Async` suffix
- [ ] Error handling at service boundaries
- [ ] Unit tests written and passing
