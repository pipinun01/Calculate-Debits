import { Search, X } from 'lucide-react'

interface Props {
  value: string
  onChange(value: string): void
  placeholder: string
}

export function SearchBar({ value, onChange, placeholder }: Props) {
  return (
    <label className="bg-card border-line shadow-soft flex h-12 items-center gap-2 rounded-2xl border px-3.5">
      <Search className="text-muted h-4 w-4 shrink-0" />
      <input
        type="search"
        inputMode="search"
        enterKeyHint="search"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className="placeholder:text-muted min-w-0 flex-1 bg-transparent text-sm outline-none [&::-webkit-search-cancel-button]:hidden"
      />
      {value && (
        <button type="button" onClick={() => onChange('')} aria-label="Clear" className="text-muted">
          <X className="h-4 w-4" />
        </button>
      )}
    </label>
  )
}
