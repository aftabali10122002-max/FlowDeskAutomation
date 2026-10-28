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

Counts are derived from records returned by authorized endpoints. Employees can edit their own name and phone number, upload or remove a profile photo, and submit one of the enabled typed request forms. Request-type-specific details and descriptions are shown in request details. Null amounts are displayed as not applicable for nonfinancial requests.

The notification menu retrieves a paginated, user-scoped feed from `/api/notifications` and listens for SignalR events at `/hubs/notifications`. The Vite development server proxies that hub with WebSocket support. Notifications remain available through the API when the realtime connection is offline; the UI displays connection state and reconnects automatically.

Requests that do not match a configured workflow generate a persistent alert for active Admins so they can assign an approver. Action feedback uses separate, temporary toasts that expire automatically and are cleared when the signed-in session changes.

Profile photos are uploaded through the authenticated profile API and stored outside the web root. The browser sends supported image files to the API; the server validates and normalizes them before returning an authenticated photo URL.

## Dashboard analytics

The Overview keeps its existing role-specific summary cards and recent-request table, with analytics loaded from the read-only `GET /api/analytics/dashboard?from=YYYY-MM-DD&to=YYYY-MM-DD` endpoint. The date values are inclusive UTC calendar dates; the API applies an inclusive start and exclusive midnight after the end date. Ranges are limited to 366 days.

The API derives the caller's active role from the database and scopes Admin results to workspace requests, Manager results to requests assigned to that Manager and decision history authored by them, and Employee results to requests owned by that Employee. Legacy requests are included for Admins and categorized as Other / Legacy. Approval durations use actual workflow activation/decision or manual assignment/decision timestamps; incomplete or reversed timestamp pairs are excluded.

Charts use Recharts and support 7-, 30-, 90-day, and custom ranges. Short ranges are bucketed daily and longer ranges monthly. Data queries are read-only and do not require a database migration.
