interface GuidanceInputProps {
  value: string;
  onChange: (value: string) => void;
}

export function GuidanceInput({ value, onChange }: GuidanceInputProps) {
  return (
    <div className="flex h-40 flex-col">
      <label htmlFor="guidance" className="mb-1 text-sm font-medium text-gray-700">
        Guidance <span className="font-normal text-gray-400">(optional)</span>
      </label>
      <textarea
        id="guidance"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder="Project size, phases to emphasize, or deviations from the historical data you want reflected…"
        className="h-full flex-1 resize-none rounded-lg border border-gray-300 p-3 text-sm text-gray-800 placeholder:text-gray-400 focus:border-blue-400 focus:outline-none"
      />
    </div>
  );
}
