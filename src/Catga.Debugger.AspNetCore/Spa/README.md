# Catga Debugger UI

Vue 3 + TypeScript + Vite frontend for Catga.Debugger.

## Tech Stack

- **Vue 3** - Composition API with `<script setup>`
- **TypeScript** - Full type safety
- **Vite** - Fast dev server and build tool
- **Pinia** - State management
- **Element Plus** - UI component library
- **Vue Router** - Client-side routing
- **SignalR** - Real-time communication
- **Axios** - HTTP client

## Project Structure

```
src/
├── api/              # API clients
│   ├── client.ts     # Axios instance
│   └── flows.ts      # Flows API
├── composables/      # Vue composables
│   └── useSignalR.ts # SignalR connection
├── stores/           # Pinia stores
│   ├── flow.ts       # Flow state
│   └── stats.ts      # Stats state
├── types/            # TypeScript types
│   └── flow.ts       # Flow types
├── views/            # Page components
│   ├── Dashboard.vue
│   ├── FlowsView.vue
│   ├── FlowDetail.vue
│   └── ReplayView.vue
├── router/           # Vue Router
│   └── index.ts
├── App.vue           # Root component
└── main.ts           # Entry point
```

## Development

```bash
# Install dependencies
npm install

# Run dev server (http://localhost:3000)
npm run dev

# Build for production
npm run build

# Type check
npm run type-check
```

## Features Implemented

### Phase 3 (Basic UI)
- ✅ Dashboard with real-time stats
- ✅ Flows list view
- ✅ Flow detail view
- ✅ Replay controls
- ✅ SignalR real-time updates
- ✅ Responsive layout

### Future Enhancements
- ⏳ Time-travel controls (slider, play/pause)
- ⏳ Macro view (system topology)
- ⏳ Micro view (step-by-step debugger)
- ⏳ Performance charts (ECharts)
- ⏳ Variable timeline visualization

## API Integration

The UI connects to the ASP.NET Core backend:

- **REST API**: `/debug-api/*` (via Axios)
- **SignalR Hub**: `/debug/hub` (real-time)

## Build Output

Built files go to `../wwwroot/` for ASP.NET Core static file serving.

## Performance

- Code splitting by route
- Manual chunks for vendor libraries
- Tree-shaking enabled
- Gzip compression ready

