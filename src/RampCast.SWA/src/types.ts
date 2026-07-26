export type FileUploadStatus = "pending" | "uploading" | "uploaded" | "failed";

export interface TrackedFile {
  id: string;
  file: File;
}
