import { useRef, useState } from "react";
import type { ChangeEvent, DragEvent } from "react";

interface UploadZoneProps {
  onFilesAdded: (files: File[]) => void;
  disabled?: boolean;
}

export function UploadZone({ onFilesAdded, disabled = false }: UploadZoneProps) {
  const [isDraggingOver, setIsDraggingOver] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  function handleDrop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    setIsDraggingOver(false);
    if (disabled) return;
    const files = Array.from(event.dataTransfer.files).filter((f) => f.name.toLowerCase().endsWith(".csv"));
    if (files.length > 0) onFilesAdded(files);
  }

  function handleInputChange(event: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.target.files ?? []);
    if (files.length > 0) onFilesAdded(files);
    event.target.value = "";
  }

  if (disabled) {
    return (
      <div className="flex h-40 cursor-not-allowed flex-col items-center justify-center rounded-lg border-2 border-dashed border-gray-200 bg-gray-100 text-center">
        <p className="text-sm font-medium text-gray-400">No uploads remaining</p>
        <p className="mt-1 text-xs text-gray-400">This token has used all of its upload grants</p>
      </div>
    );
  }

  return (
    <div
      className="group relative flex h-40 cursor-pointer flex-col items-center justify-center rounded-lg border-2 border-dashed border-gray-300 bg-gray-50 text-center transition-colors hover:border-blue-400 hover:bg-blue-50"
      onClick={() => inputRef.current?.click()}
      onDragOver={(event) => {
        event.preventDefault();
        setIsDraggingOver(true);
      }}
      onDragLeave={() => setIsDraggingOver(false)}
      onDrop={handleDrop}
    >
      <input
        ref={inputRef}
        type="file"
        accept=".csv"
        multiple
        className="hidden"
        onChange={handleInputChange}
      />
      <p className="text-sm font-medium text-gray-600 group-hover:text-blue-700">
        Drag & drop timesheet CSVs here
      </p>
      <p className="mt-1 text-xs text-gray-400 group-hover:text-blue-500">or click to browse</p>
      {isDraggingOver && (
        <div className="absolute inset-0 flex items-center justify-center rounded-lg bg-blue-100/80 text-sm font-semibold text-blue-700">
          Drop CSVs here
        </div>
      )}
    </div>
  );
}
