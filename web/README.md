# FlowDesk dashboard

Responsive React and TypeScript frontend for the FlowDesk Approval Management API.

## Local development

The dashboard expects the API at `http://localhost:5192`. Vite proxies `/api` requests to that address:

```powershell
npm install
npm run dev
```

For a different local API address, update `server.proxy['/api'].target` in `vite.config.ts`. In production, serve the frontend and API from the same origin or configure a narrowly scoped CORS policy and an explicit API base URL.

## Build and lint

```powershell
npm run build
npm run lint
```

The JWT is kept in `sessionStorage` for the active browser tab session and removed after sign-out or an unauthorized response. The API remains responsible for authentication and authorization; frontend role-specific navigation is not a security boundary.

Counts are derived from records returned by authorized endpoints. The UI intentionally does not offer user creation, profile editing, or request categories because those capabilities are not exposed by the current API. The notification feed does support server-backed pagination.

The notification menu retrieves a paginated, user-scoped feed from `/api/notifications` and listens for SignalR events at `/hubs/notifications`. The Vite development server proxies that hub with WebSocket support. Notifications remain available through the API when the realtime connection is offline; the UI displays connection state and reconnects automatically.
