import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { useAccessToken } from "../context/AccessTokenContext";

export function TokenGate() {
  const { signIn } = useAccessToken();
  const [value, setValue] = useState("");

  const mutation = useMutation<void, Error, string>({
    mutationFn: (raw) => signIn(raw),
  });

  function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (value.trim().length === 0) return;
    mutation.mutate(value.trim());
  }

  return (
    <div className="mx-auto max-w-sm rounded-lg border border-gray-300 bg-gray-50 p-6">
      <h2 className="text-sm font-semibold text-gray-800">Enter your access token</h2>
      <p className="mt-1 text-xs text-gray-500">
        Paste the token you were given to unlock uploads and analysis.
      </p>
      <form onSubmit={handleSubmit} className="mt-4 flex flex-col gap-2">
        <input
          type="text"
          value={value}
          onChange={(event) => setValue(event.target.value)}
          placeholder="00000000-0000-0000-0000-000000000000"
          autoComplete="off"
          spellCheck={false}
          className="rounded-lg border border-gray-300 p-3 font-mono text-sm text-gray-800 placeholder:text-gray-400 focus:border-blue-400 focus:outline-none"
        />
        <button
          type="submit"
          disabled={value.trim().length === 0 || mutation.isLoading}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-gray-300"
        >
          {mutation.isLoading ? "Checking…" : "Continue"}
        </button>
        {mutation.isError && <p className="text-sm text-red-700">{mutation.error.message}</p>}
      </form>
    </div>
  );
}
