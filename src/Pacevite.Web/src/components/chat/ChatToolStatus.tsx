interface Props {
  label: string
}

export function ChatToolStatus({ label }: Props) {
  return (
    <div className="flex justify-start mb-3">
      <div className="w-7 h-7 rounded-full bg-blue-600 flex items-center justify-center text-white text-xs font-bold mr-2 flex-shrink-0">
        P
      </div>
      <div className="bg-gray-100 rounded-2xl rounded-tl-sm px-4 py-2 text-sm text-gray-500 flex items-center gap-2">
        <span className="flex gap-1">
          <span className="w-1.5 h-1.5 bg-gray-400 rounded-full animate-bounce [animation-delay:0ms]" />
          <span className="w-1.5 h-1.5 bg-gray-400 rounded-full animate-bounce [animation-delay:150ms]" />
          <span className="w-1.5 h-1.5 bg-gray-400 rounded-full animate-bounce [animation-delay:300ms]" />
        </span>
        {label}
      </div>
    </div>
  )
}
