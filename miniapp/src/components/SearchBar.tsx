import { Search, X } from 'lucide-react'

interface Props {
  value: string
  onChange(value: string): void
  placeholder: string
}

export function SearchBar({ value, onChange, placeholder }: Props) {
  return (
    <label className="bg-card border-line shadow-soft focus-within:border-accent focus-within:ring-accent/25 flex h-11 items-center gap-2.5 rounded-2xl border px-3.5 transition focus-within:ring-2">
      <Search className="text-muted dark:text-accent h-4 w-4 shrink-0" />
      <input
        type="search"
        inputMode="search"
        enterKeyHint="search"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className="placeholder:text-muted min-w-0 flex-1 bg-transparent text-base outline-none md:text-sm [&::-webkit-search-cancel-button]:hidden"
      />
      {value && (
        <button type="button" onClick={() => onChange('')} aria-label="Clear" className="text-muted -mr-1 flex h-8 w-8 items-center justify-center">
          <X className="h-4 w-4" />
        </button>
      )}
    </label>
  )
}
