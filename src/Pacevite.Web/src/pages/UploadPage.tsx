import { useRef, useState, type ChangeEvent } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import { ThemeToggle } from '@/components/ThemeToggle'
import { ArrowLeft, FileUp, ChartNoAxesColumn } from 'lucide-react'
import type { EventResponse } from '@/lib/types'

export function UploadPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const inputRef = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: async (f: File) => {
      const form = new FormData()
      form.append('file', f)
      const { data } = await apiClient.post<EventResponse[]>('/events/upload', form)
      return data
    },
    onSuccess: (created) => {
      void queryClient.invalidateQueries({ queryKey: ['events'] })
      void queryClient.invalidateQueries({ queryKey: ['personal-bests'] })
      navigate('/dashboard', {
        state: { uploadedCount: created.length },
      })
    },
    onError: () => {
      setError('Upload failed. Check your file format and try again.')
    },
  })

  function handleFileChange(e: ChangeEvent<HTMLInputElement>) {
    const selected = e.target.files?.[0] ?? null
    setFile(selected)
    setError(null)
  }

  function handleSubmit(e: { preventDefault(): void }) {
    e.preventDefault()
    if (!file) return
    mutation.mutate(file)
  }

  return (
    <div className="min-h-screen bg-bg">
      <header className="bg-surface border-b border-border px-6 py-4 flex items-center gap-4">
        <Link to="/dashboard" className="text-secondary hover:text-primary">
          <ArrowLeft size={16} />
        </Link>
        <h1 className="text-lg font-semibold text-primary flex-1">Upload Events</h1>
        <Link
          to="/predict"
          className="inline-flex items-center gap-2 text-sm text-secondary hover:text-primary"
        >
          <ChartNoAxesColumn size={14} /> Predict
        </Link>
        <ThemeToggle />
      </header>

      <main className="max-w-xl mx-auto px-6 py-12 space-y-6">
        <div className="bg-surface rounded-lg border border-border p-8 space-y-6">
          <div>
            <h2 className="text-base font-semibold text-primary">Supported formats</h2>
            <ul className="mt-2 text-sm text-secondary space-y-1 list-disc list-inside">
              <li><strong>CSV</strong> — event_type, event_name, event_date (yyyy-MM-dd), completion, elapsed_secs</li>
              <li><strong>JSON</strong> — array of event objects with the same fields</li>
            </ul>
          </div>

          <form onSubmit={handleSubmit} className="space-y-4">
            <div
              className="border-2 border-dashed border-border rounded-lg p-8 text-center cursor-pointer hover:border-primary transition-colors"
              onClick={() => inputRef.current?.click()}
            >
              <FileUp size={24} className="mx-auto text-muted mb-2" />
              {file ? (
                <p className="text-sm font-medium text-primary">{file.name}</p>
              ) : (
                <p className="text-sm text-secondary">Click to select a CSV or JSON file</p>
              )}
              <input
                ref={inputRef}
                type="file"
                accept=".csv,text/csv,application/json,.json"
                onChange={handleFileChange}
                className="hidden"
              />
            </div>

            {error && <p className="text-sm text-red-600">{error}</p>}

            <button
              type="submit"
              disabled={!file || mutation.isPending}
              className="w-full bg-action text-action-fg rounded-md px-4 py-2 text-sm font-medium hover:bg-action-hover disabled:opacity-50"
            >
              {mutation.isPending ? 'Uploading…' : 'Upload'}
            </button>
          </form>
        </div>
      </main>
    </div>
  )
}
