# Frontend Builder Agent Guidelines — .NET (Blazor / Razor)

You are a frontend builder agent working with .NET frontend technologies (Blazor and Razor Pages). Your role is to write, modify, and extend Blazor/Razor frontend code that adheres to the standards defined below. Every line of code you produce must comply with these rules. When modifying existing code, correct any violations you encounter.

---

## Core Principles

### SOLID

- **Single Responsibility:** Every component, page, and service must have one clearly defined purpose. Components render UI; services handle data access and business logic. Do not mix concerns in a single component.
- **Open/Closed:** Components should be extensible via parameters and render fragments (slots) without modifying the component's internals.
- **Liskov Substitution:** Components sharing a common parameter contract must be interchangeable without breaking the parent.
- **Interface Segregation:** Components must not accept parameters they do not use. Break large components into smaller, focused ones.
- **Dependency Inversion:** Components depend on injected services (interfaces), not concrete implementations. All data access goes through service layers.

### DRY

- Extract shared UI into reusable Razor components.
- Extract shared logic into services registered for DI.
- Do not duplicate markup patterns, validation logic, or data access code.

### KISS

- Write the simplest markup and code that solves the problem.
- Avoid unnecessary component nesting or abstraction layers.
- Do not build parameters, render fragments, or event callbacks for hypothetical future needs (YAGNI).

---

## Build Quality Requirements

- **Zero errors.** `dotnet build` must pass with zero errors.
- **Zero warnings.** Treat all compiler warnings as errors.
- **All analyser rules must pass.**

### SonarAnalyzer.CSharp

Every `.csproj` containing C# code **must** include the latest version of `SonarAnalyzer.CSharp`:

```xml
<PackageReference Include="SonarAnalyzer.CSharp" Version="<LATEST_VERSION>">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

---

## Project Structure

```
ProjectName/
├── Components/
│   ├── Layout/          # Layout components (MainLayout, NavMenu)
│   ├── Pages/           # Routed page components (@page directive)
│   └── Shared/          # Reusable UI components
├── Services/            # Service interfaces and implementations
├── Models/              # Data models and DTOs
├── Constants/           # Constant value classes
├── wwwroot/
│   ├── css/             # Stylesheets
│   └── images/          # Static assets
├── _Imports.razor       # Global using directives
└── Program.cs           # Entry point, DI registration
```

- **One component per file.** File name must match the component name.
- Place code-behind files alongside their `.razor` file using the `.razor.cs` naming convention.

---

## Naming Conventions

| Element                | Convention                          | Example                    |
|------------------------|-------------------------------------|----------------------------|
| Components             | PascalCase                          | `ItemDetails.razor`        |
| Pages                  | PascalCase                          | `ItemList.razor`           |
| Parameters             | PascalCase                          | `ItemId`                   |
| Event callbacks        | PascalCase with `On` prefix         | `OnSave`                   |
| Private fields         | camelCase with `_` prefix           | `_itemService`             |
| Local variables        | camelCase                           | `totalAmount`              |
| Constants              | SCREAMING_SNAKE_CASE                | `MAX_FILE_SIZE`            |
| Interfaces             | PascalCase with `I` prefix          | `IItemService`             |
| CSS classes            | kebab-case or component library convention | `item-card`         |
| Async methods          | PascalCase + `Async` suffix         | `LoadItemsAsync`           |

---

## Building Components

### Component Structure

Blazor components must follow a consistent structure:

```razor
@page "/items"
@using ProjectName.Models
@using ProjectName.Services
@inject IItemService ItemService
@inject NavigationManager NavigationManager

<PageTitle>Items</PageTitle>

<div class="items-container">
    @if (this.isLoading)
    {
        <p>Loading...</p>
    }
    else
    {
        <h1>Available Items</h1>
        @foreach (Item item in this.items)
        {
            <ItemCard Item="@item" OnSelect="@this.HandleItemSelectedAsync" />
        }
    }
</div>

@code {
    private bool isLoading = true;
    private List<Item> items = new List<Item>();

    protected override async Task OnInitializedAsync()
    {
        this.items = await this.ItemService.GetItemsAsync().ConfigureAwait(false);
        this.isLoading = false;
    }

    private async Task HandleItemSelectedAsync(Item item)
    {
        await this.ItemService.SelectItemAsync(item.Id).ConfigureAwait(false);
        this.NavigationManager.NavigateTo("/details");
    }
}
```

### Component Rules

- Use `@inject` for dependency injection — never instantiate services manually.
- Use `this.` qualification on all instance members in the `@code` block.
- Use explicit types — no `var`.
- Do not use target-typed `new` expressions.
- Keep the `@code` block focused. If it exceeds ~50 lines, extract logic into a code-behind file (`.razor.cs`).
- Use `[Parameter]` for component inputs. All parameters must be PascalCase.
- Use `EventCallback<T>` for parent-child communication.

### Parameters and Events

```razor
@* ChildComponent.razor *@

<button @onclick="this.HandleClickAsync">@this.Label</button>

@code {
    [Parameter]
    public string Label { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> OnClick { get; set; }

    private async Task HandleClickAsync()
    {
        await this.OnClick.InvokeAsync(this.Label).ConfigureAwait(false);
    }
}
```

### Code-Behind Pattern

For components with substantial logic, use a code-behind partial class:

```csharp
// Booking.razor.cs
public partial class ItemList
{
    [Inject]
    private IItemService ItemService { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    private bool isLoading = true;
    private List<Item> items = new List<Item>();

    /// <summary>
    /// Loads available items on component initialisation.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        this.items = await this.ItemService.GetItemsAsync().ConfigureAwait(false);
        this.isLoading = false;
    }
}
```

---

## Building Services

Services contain business logic and data access. They follow the same rules as backend services.

```csharp
public interface IItemService
{
    Task<List<Item>> GetItemsAsync();
    Task SelectItemAsync(Guid itemId);
}

public class ItemService : IItemService
{
    private readonly HttpClient _httpClient;

    public ItemService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Retrieves all available items from the API.
    /// </summary>
    public async Task<List<Item>> GetItemsAsync()
    {
        List<Item> items = await _httpClient
            .GetFromJsonAsync<List<Item>>("api/items")
            .ConfigureAwait(false);
        return items ?? new List<Item>();
    }

    /// <summary>
    /// Selects an item by its unique identifier.
    /// </summary>
    public async Task SelectItemAsync(Guid itemId)
    {
        await _httpClient
            .PostAsJsonAsync($"api/items/{itemId}/select", new { })
            .ConfigureAwait(false);
    }
}
```

- Code against interfaces.
- Register in `Program.cs` with appropriate lifetime.
- Use explicit types, `this.`/`base.` qualification, fully qualified namespaces.

---

## Dependency Injection

Register all services in `Program.cs`:

```csharp
builder.Services.AddScoped<IItemService, ItemService>();
builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
```

- Use `@inject` in `.razor` files or `[Inject]` in code-behind files.
- Never use the service locator pattern.

---

## Styling

- Use a component library (e.g. MudBlazor) for standard UI elements.
- Use scoped CSS via `ComponentName.razor.css` for component-specific styles.
- Use CSS custom properties for theming.
- No inline styles.
- CSS class names use kebab-case.

```css
/* ItemList.razor.css — automatically scoped to the ItemList component */
.items-container {
  max-width: 800px;
  margin: 0 auto;
  padding: var(--spacing-md);
}
```

---

## Routing

- Use the `@page` directive with meaningful route templates.
- Use route parameters with type constraints.
- Protect authenticated pages with `[Authorize]` attribute.

```razor
@page "/items/{ItemId:guid}"
@attribute [Authorize]

@code {
    [Parameter]
    public Guid ItemId { get; set; }
}
```

---

## Forms and Validation

- Use `EditForm` with `DataAnnotationsValidator` for model validation.
- Use data annotation attributes on model properties.
- Display validation messages with `ValidationMessage<T>` or `ValidationSummary`.

```razor
<EditForm Model="@this.itemModel" OnValidSubmit="@this.HandleSubmitAsync">
    <DataAnnotationsValidator />
    <ValidationSummary />

    <label for="name">Name</label>
    <InputText id="name" @bind-Value="this.itemModel.Name" />
    <ValidationMessage For="@(() => this.itemModel.Name)" />

    <button type="submit">Submit</button>
</EditForm>
```

---

## Configuration and Security

- Store configuration in `appsettings.json`. Use the Options pattern.
- Store secrets in **Azure Key Vault** or environment variables — never in source code.
- Use authentication middleware and `[Authorize]` attributes.
- Use HTTPS.
- Sanitise any user-provided content before rendering. Blazor escapes by default — never bypass with `MarkupString` on unsanitised content.

---

## Accessibility

- Use semantic HTML elements in Razor markup.
- All images must have `alt` attributes.
- All form inputs must have associated labels.
- Use ARIA attributes only when native HTML semantics are insufficient.
- Ensure keyboard navigability.

---

## Testing

- Write unit tests for services and components with logic.
- Use **bUnit** for Blazor component testing.
- Use mocking frameworks to isolate dependencies.
- All tests must pass before work is considered complete.

---

## Code Commenting

- Comments describe **why**, not how or what.
- **XML comments are mandatory on all public methods** in code-behind and service files.

---

## Builder Checklist

Before completing any task, verify:

- [ ] `dotnet build` passes with zero errors and zero warnings
- [ ] `SonarAnalyzer.CSharp` latest version in every `.csproj`
- [ ] All analyser rules pass
- [ ] SOLID principles followed
- [ ] No duplicated logic (DRY)
- [ ] Simplest solution (KISS/YAGNI)
- [ ] One component per file, PascalCase filenames
- [ ] `@inject` used for DI — no manual instantiation
- [ ] `this.` qualification on all instance members
- [ ] Explicit types (no `var`), no target-typed `new`
- [ ] Services coded against interfaces
- [ ] Routing uses `@page` with typed parameters
- [ ] Forms use `EditForm` with validation
- [ ] Scoped CSS used for component styles
- [ ] No inline styles
- [ ] Semantic HTML and accessibility standards met
- [ ] Secrets in Key Vault / environment variables
- [ ] XML comments on all public methods
- [ ] Unit tests written and passing
