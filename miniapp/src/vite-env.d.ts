/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_DEV_TELEGRAM_ID?: string
  readonly VITE_DEV_FIRST_NAME?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
