# syntax=docker/dockerfile:1

# ---- Stage 1: build the SPA with Node ----
FROM node:22-alpine AS build
WORKDIR /app

# Install dependencies first (better layer caching). Use npm ci when a lockfile
# is present; fall back to npm install on a clean checkout without one.
COPY package.json package-lock.json* ./
RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi

# Copy the rest of the source and build. The runtime API base is the relative
# '/api' (no per-environment rebuild needed — ADR-0005), so no build-time env
# is required here.
COPY . .
RUN npm run build

# ---- Stage 2: serve static assets with nginx ----
FROM nginx:alpine AS runtime

# Replace the default site config with our SPA + /api proxy config.
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Copy the compiled SPA into nginx's web root.
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

# nginx:alpine already runs nginx in the foreground via its default CMD.
CMD ["nginx", "-g", "daemon off;"]
