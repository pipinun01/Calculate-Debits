import i18n from 'i18next'
import HttpBackend from 'i18next-http-backend'
import { initReactI18next } from 'react-i18next'
import type { Language } from '../api/types'

export const LANGUAGES: Language[] = ['uz-Latn', 'uz-Cyrl', 'ru']

void i18n
  .use(HttpBackend)
  .use(initReactI18next)
  .init({
    lng: 'uz-Latn',
    fallbackLng: 'uz-Latn',
    supportedLngs: LANGUAGES,
    load: 'currentOnly',
    backend: { loadPath: `${import.meta.env.BASE_URL}locales/{{lng}}/translation.json` },
    // Flat keys ("errors.customer.exists"): backend error codes contain dots and map straight to keys.
    keySeparator: false,
    nsSeparator: false,
    interpolation: { escapeValue: false },
    returnNull: false,
  })

export default i18n
