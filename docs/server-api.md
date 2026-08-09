# API CyRevision Server

Toutes les routes `/api/v1` sauf `capabilities` demandent :

```http
Authorization: Bearer <token>
```

Principales routes :

- `GET /health`
- `GET /api/v1/capabilities`
- `GET|POST /api/v1/projects`
- `DELETE /api/v1/projects/{id}`
- `GET /api/v1/projects/{id}/git/status`
- `GET /api/v1/projects/{id}/git/history`
- `POST /api/v1/projects/{id}/git/exchange`
- `GET|POST /api/v1/projects/{id}/backups`
- `POST /api/v1/projects/{id}/sync/configure`
- `POST /api/v1/projects/{id}/sync/start|pause|stop`
- `GET /api/v1/projects/{id}/sync/status`
- `POST /api/v1/projects/{id}/peers/invitations`
- `POST /api/v1/projects/{id}/peers/join-request`
- `POST /api/v1/projects/{id}/peers/approve`
- `POST /api/v1/projects/{id}/peers/membership`
- `GET /api/v1/projects/{id}/peers`
- `DELETE /api/v1/projects/{id}/peers/{deviceId}`

Le dashboard embarqué utilise la même API. Le token reste dans `sessionStorage` et disparaît à la fermeture de l’onglet.
