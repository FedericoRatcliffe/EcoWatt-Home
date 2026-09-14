FROM node:24-alpine AS build
WORKDIR /src

COPY package*.json ./
RUN npm ci

COPY . .
RUN npx ng build

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/ecowatt-frontend/browser /usr/share/nginx/html
EXPOSE 80
