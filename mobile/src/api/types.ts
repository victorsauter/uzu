/**
 * API shapes used across the app, taken from the generated `schema.d.ts`
 * (`api/openapi.json`). Only RFC 9457 problem details keep a local shape, with
 * the Uzu `code` extension (spec §1), because errors are read loosely.
 */
import type { components } from './schema';

type Schemas = components['schemas'];

/** `user` in the session response: private profile of the signed-in user. */
export type SessionUser = Schemas['MeResponse'];

/** Response of `POST /api/v1/auth/{apple,google,dev,refresh}`. */
export type AuthResponse = Schemas['SessionResponse'];

export type Dog = Schemas['DogResponse'];
/** One human linked to a dog: their summary and their role (only `Owner` in V1). */
export type DogMember = Schemas['DogMemberResponse'];
export type PublicUser = Schemas['PublicUserResponse'];
/** A pending invitation to a dog, listed to its owners (no token). */
export type DogInvitation = Schemas['DogInvitationResponse'];
/** An invitation just created: the only response that carries the token and the link. */
export type CreatedDogInvitation = Schemas['CreatedDogInvitationResponse'];
/** What the person invited sees before accepting. */
export type DogInvitationPreview = Schemas['DogInvitationPreviewResponse'];
export type DogMemberRole = Schemas['DogMemberRole'];
export type CreateDogRequest = Schemas['CreateDogRequest'];
export type UpdateDogRequest = Schemas['UpdateDogRequest'];

export type GeoPoint = Schemas['GeoPoint'];
export type UserSummary = Schemas['UserSummaryResponse'];
export type WalkSearchResult = Schemas['WalkSearchResult'];
export type WalkDetail = Schemas['WalkDetailResponse'];
export type MyWalk = Schemas['MyWalkResponse'];
export type Participant = Schemas['ParticipantResponse'];
export type MyRegistration = Schemas['MyRegistrationResponse'];
/** A registration of one of my dogs, whichever owner made it (`registeredBy`). */
export type DogRegistration = Schemas['DogRegistrationResponse'];
export type Registration = Schemas['RegistrationResponse'];
export type CreateWalkRequest = Schemas['CreateWalkRequest'];
export type UpdateWalkRequest = Schemas['UpdateWalkRequest'];
export type Message = Schemas['MessageResponse'];
export type MessagePage = Schemas['MessagePageResponse'];
export type NotificationPreferences = Schemas['NotificationPreferencesResponse'];
export type DevicePlatform = Schemas['DevicePlatform'];
export type OrganizationSummary = Schemas['OrganizationSummaryResponse'];
export type OrganizationDetail = Schemas['OrganizationDetailResponse'];
export type MyOrganization = Schemas['MyOrganizationResponse'];
export type MyMembership = Schemas['MyMembershipResponse'];
export type Membership = Schemas['MembershipResponse'];
export type CreateOrganizationRequest = Schemas['CreateOrganizationRequest'];
export type UpdateOrganizationRequest = Schemas['UpdateOrganizationRequest'];
export type BlockedUser = Schemas['BlockResponse'];
export type CreateReportRequest = Schemas['CreateReportRequest'];

/** Fields every walk shape shares (search result, my walks, detail): what `WalkCard` shows. */
export type WalkSummary = Pick<
  WalkSearchResult,
  | 'id'
  | 'title'
  | 'startsAt'
  | 'durationMinutes'
  | 'location'
  | 'locationLabel'
  | 'maxDogs'
  | 'acceptedDogsCount'
  | 'allowedSizes'
  | 'leashRule'
  | 'access'
  | 'status'
  | 'organizer'
  | 'isProfessional'
  | 'organization'
  | 'hasBlockedParticipant'
  | 'repeatsWeekly'
> & {
  distanceMeters?: number;
  myRegistrations?: MyRegistration[];
};

/** RFC 9457 problem details, with the Uzu `code` extension (spec §1). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  code?: string;
  /** Validation errors: field name → messages. */
  errors?: Record<string, string[]>;
}
