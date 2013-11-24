#region Copyright(C)  Licensed under GNU GPL.
/// Copyright (C) 2005-2006 Agustin Santos Mendez
/// 
/// JSBSim was developed by Jon S. Berndt, Tony Peden, and
/// David Megginson. 
/// Agustin Santos Mendez implemented and maintains this C# version.
/// 
/// This program is free software; you can redistribute it and/or
/// modify it under the terms of the GNU General Public License
/// as published by the Free Software Foundation; either version 2
/// of the License, or (at your option) any later version.
///  
/// This program is distributed in the hope that it will be useful,
/// but WITHOUT ANY WARRANTY; without even the implied warranty of
/// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
/// GNU General Public License for more details.
///  
/// You should have received a copy of the GNU General Public License
/// along with this program; if not, write to the Free Software
/// Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
#endregion

namespace JSBSim
{
	using System;

	// Import log4net classes.
	using log4net;

	using CommonUtils.MathLib;

	/// <summary>
	/// Holds an arbitrary location in the earth centered reference frame.
	/// This coordinate frame has its center in the middle of the earth.
	/// Its x-axis points from the center of the earth towards a location
	/// with zero latitude and longitude on the earths surface. The y-axis
	/// points from the center of the earth towards a location with zero
	/// latitude and 90deg longitude on the earths surface. The z-axis
	/// points from the earths center to the geographic north pole.
	/// 
	/// This class provides access functions to set and get the location as
	/// either the simple x, y and z values in ft or longitude/latitude and
	/// the radial distance of the location from the earth center.
	///
	/// It is common to associate a parent frame with a location. This
	/// frame is usually called the local horizontal frame or simply the local
	/// frame. This frame has its x/y plane parallel to the surface of the earth
	/// (with the assumption of a spherical earth). The x-axis points
	/// towards north, the y-axis points towards east and the z-axis
	/// points to the center of the earth.
	///
	/// Since this frame is determined by the location, this class also
	/// provides the rotation matrices required to transform from the
	/// earth centered frame to the local horizontal frame and back. There
	/// are also conversion functions for conversion of position vectors
	/// given in the one frame to positions in the other frame.
	///
	/// The earth centered reference frame is *NOT* an inertial frame
	/// since it rotates with the earth.
	///
	/// The coordinates in the earth centered frame are the master values.
	/// All other values are computed from these master values and are
	/// cached as long as the location is changed by access through a
	/// non-const member function. Values are cached to improve performance.
	/// It is best practice to work with a natural set of master values.
	/// Other parameters that are derived from these master values are calculated
	/// only when needed, and IF they are needed and calculated, then they are
	/// cached (stored and remembered) so they do not need to be re-calculated
	/// until the master values they are derived from are themselves changed
	/// (and become stale).
	/// 
	/// Accuracy and round off:
	/// 
	/// Given that we model a vehicle near the earth, the earths surface
	/// radius is about 2*10^7, ft and that we use double values for the
	/// representation of the location, we have an accuracy of about
	/// 1e-16*2e7ft/1=2e-9ft left. This should be sufficient for our needs.
	/// Note that this is the same relative accuracy we would have when we
	/// compute directly with lon/lat/radius. For the radius value this
	/// is clear. For the lon/lat pair this is easy to see. Take for
	/// example KSFO located at about 37.61deg north 122.35deg west, which
	/// corresponds to 0.65642rad north and 2.13541rad west. Both values
	/// are of magnitude of about 1. But 1ft corresponds to about
	/// 1/(2e7*2*pi)=7.9577e-09rad. So the left accuracy with this
	/// representation is also about 1*1e-16/7.9577e-09=1.2566e-08 which
	/// is of the same magnitude as the representation chosen here.
	/// 
	/// The advantage of this representation is that it is a linear space
	/// without singularities. The singularities are the north and south
	/// pole and most notably the non-steady jump at -pi to pi. It is
	/// harder to track this jump correctly especially when we need to
	/// work with error norms and derivatives of the equations of motion
	/// within the time-stepping code. Also, the rate of change is of the
	/// same magnitude for all components in this representation which is
	/// an advantage for numerical stability in implicit time-stepping too.
	/// 
	/// Note: The latitude is a GEOCENTRIC value. FlightGear
	/// converts latitude to a geodetic value and uses that. In order to get best
	/// matching relative to a map, geocentric latitude must be converted to geodetic.
	/// 
	/// see W. C. Durham "Aircraft Dynamics & Control", section 2.2
	/// 
	/// this code is based on FGLocation class written by Jon S. Berndt, Mathias Froehlich
	/// </summary>
	public class Location
	{
        /// <summary>
        /// Define a static logger variable so that it references the
        ///	Logger instance.
        /// 
        /// NOTE that using System.Reflection.MethodBase.GetCurrentMethod().DeclaringType
        /// is equivalent to typeof(LoggingExample) but is more portable
        /// i.e. you can copy the code directly into another class without
        /// needing to edit the code.
        /// </summary>
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		public Location() 
		{
			mCacheValid = false; 
		}

		public Location(Vector3D lv)
		{
			mECLoc = lv;
			mCacheValid = false;
		}

		/// <summary>
		/// Constructor to set the longitude, latitude and the distance
		/// from the center of the earth.
		/// </summary>
		/// <param name="lon">longitude</param>
		/// <param name="lat">GEOCENTRIC latitude</param>
		/// <param name="radius">distance from center of earth to vehicle in feet</param>
		public Location(double lon, double lat, double radius)
		{
			mCacheValid = false;
			double sinLat = Math.Sin(lat);
			double cosLat = Math.Cos(lat);
			double sinLon = Math.Sin(lon);
			double cosLon = Math.Cos(lon);
			mECLoc = new Vector3D( radius*cosLat*cosLon,	radius*cosLat*sinLon, radius*sinLat );
		}
		
		/// <summary>
		/// Get/set the longitude.
		/// 
		/// Return the longitude in rad of the location represented with this
		/// class instance. The returned values are in the range between
        /// <code>-pi <= lon <= pi</code>.
        /// Longitude is positive east and negative west.
		/// Sets the longitude of the location represented with this class
		/// instance to the value of the given argument. The value is meant
		/// to be in rad. The latitude and the radius value are preserved
		/// with this call with the exception of radius being equal to
		/// zero. If the radius is previously set to zero it is changed to be
		/// equal to 1.0 past this call. Longitude is positive east and negative west.
		/// </summary>
		public double Longitude 
		{
			get {ComputeDerived(); return mLon; }
			set 
			{ 
				double rtmp = mECLoc.GetMagnitude((int)PositionType.eX, (int)PositionType.eY);
				// Check if we have zero radius.
				// If so set it to 1, so that we can set a position
				if (0.0 == mECLoc.GetMagnitude())
					rtmp = 1.0;

				// Fast return if we are on the north or south pole ...
				if (rtmp == 0.0)
					return;

				mCacheValid = false;

				mECLoc.X = rtmp*Math.Cos(value);
				mECLoc.Y = rtmp*Math.Sin(value);
			}
		}

		/// <summary>
		/// Get the longitude.
		/// return the longitude in deg of the location represented with this
		/// class instance. The returned values are in the range between
        /// <code>-180 <= lon <= 180</code>.
        /// Longitude is positive east and negative west.
		/// </summary>
		public double LongitudeDeg { get {ComputeDerived(); return Constants.radtodeg*mLon; }}


		/// <summary>
		/// Get the sine of Longitude. 
		/// </summary>
		public double SinLongitude { get {ComputeDerived(); return -mTec2l.M21; }}

		/// <summary>
		/// Get the cosine of Longitude. 
		/// </summary>
		public double CosLongitude { get { ComputeDerived(); return mTec2l.M22; }}

		
		/// <summary>
		/// Get/Set the latitude.
		/// 
		/// Return the latitude in rad of the location represented with this
		/// class instance. The returned values are in the range between
        /// <code>-pi/2 <= lon <= pi/2</code>.
        /// Latitude is positive north and negative south.
		/// Sets the latitude of the location represented with this class
		/// instance to the value of the given argument. The value is meant
		/// to be in rad. The longitude and the radius value are preserved
		/// with this call with the exception of radius being equal to
		/// zero. If the radius is previously set to zero it is changed to be
		/// equal to 1.0 past this call.
		/// Latitude is positive north and negative south.
		/// The arguments should be within the bounds of -pi/2 <= lat <= pi/2.
		/// The behavior of this function with arguments outside this range is
		/// left as an exercise to the gentle reader ...
		/// </summary>
		public double Latitude 
		{
			get { ComputeDerived(); return mLat; }
			set 
			{
				mCacheValid = false;

				double r = mECLoc.GetMagnitude();
				if (r == 0.0) 
				{
					mECLoc.X = 1.0;
					r = 1.0;
				}

				double rtmp = mECLoc.GetMagnitude((int)PositionType.eX, (int)PositionType.eY);
				if (rtmp != 0.0) 
				{
					double fac = r/rtmp*Math.Cos(value);
					mECLoc.X *= fac;
					mECLoc.Y *= fac;
				} 
				else 
				{
					mECLoc.X = r*Math.Cos(value);
					mECLoc.Y = 0.0;
				}
				mECLoc.Z = r*Math.Sin(value);
			}
		}

		/// <summary>
		/// Get the latitude
		/// Return the latitude in deg of the location represented with this
		/// class instance. The returned values are in the range between
        /// <code>-90 <= lon <= 90</code>.
        /// Latitude is positive north and negative south.
		/// </summary>
		public double LatitudeDeg { get {ComputeDerived(); return Constants.radtodeg*mLat; }}

		/// <summary>
		/// Get the sine of Latitude.
		/// </summary>
		public double SinLatitude { get {ComputeDerived(); return -mTec2l.M33; }}
		
		/// <summary>
		/// Get the cosine of Latitude.
		/// </summary>
		public double CosLatitude { get {ComputeDerived(); return mTec2l.M13; }}

		/// <summary>
		/// Get the Tan of Latitude
		/// </summary>
		public double TanLatitude
		{
			get 
			{
				ComputeDerived();
				double cLat = mTec2l.M13;
				if (cLat == 0.0)
					return 0.0;
				else
					return -mTec2l.M33/cLat;
			}
		}

		/// <summary>
		/// Get/Set the distance from the center of the earth.
		/// return the distance of the location represented with this class
		/// instance to the center of the earth in ft. The radius value is
		/// always positive.
		/// Sets the radius of the location represented with this class
		/// instance to the value of the given argument. The value is meant
		/// to be in ft. The latitude and longitude values are preserved
		/// with this call with the exception of radius being equal to
		/// zero. If the radius is previously set to zero, latitude and
		/// longitude is set equal to zero past this call.
		/// The argument should be positive.
		/// The behavior of this function called with a negative argument is
		/// left as an exercise to the gentle reader ... 
		/// </summary>
		public double Radius 
		{
			get { ComputeDerived(); return mRadius; }
			set
			{
				mCacheValid = false;

				double rold = mECLoc.GetMagnitude();
				if (rold == 0.0)
					mECLoc.X = value;
				else
					mECLoc *= value/rold;
			}
		}

		/// <summary>
		/// Subtract two locations.
		/// </summary>
		/// <param name="vec1">The location to substract from.</param>
		/// <param name="vec2">The location to substract.</param>
		/// <returns>Result is ( vec1.X - vec2.X, vec1.Y - vec2.Y, vec1.Z - vec2.Z )</returns>
		public static Location operator-(Location vec1, Location vec2) 
		{
			return new Location(vec1.mECLoc - vec2.mECLoc);;
		}

		/// <summary>
		/// Add two locations.
		/// </summary>
		/// <param name="vec1">The first location to add.</param>
		/// <param name="vec2">The second location to add.</param>
		/// <returns>Result is ( vec1.X + vec2.X, vec1.Y + vec2.Y, vec1.Z + vec2.Z )</returns>
		public static Location operator+(Location vec1, Location vec2) 
		{
			return	new Location( vec1.mECLoc + vec2.mECLoc);
		}

		/// <summary>
		/// Multiply Location <paramref name="vec"/> by a double value <paramref name="f"/>.
		/// </summary>
		/// <param name="f">The double value.</param>
		/// <param name="loc">The Location.</param>
		/// <returns>Result is ( vec.X*f, vec.Y*f, vec.Z*f ).</returns>
		public static Location operator*(double f, Location loc) 
		{
			return	new Location(loc.mECLoc * f);
		}

		/// <summary>
		/// Multiply Location <paramref name="vec"/> by a double value <paramref name="f"/>.
		/// </summary>
		/// <param name="f">The double value.</param>
		/// <param name="loc">The Location.</param>
		/// <returns>Result is ( vec.X*f, vec.Y*f, vec.Z*f ).</returns>
		public static Location operator*(Location loc, double f) 
		{
			return	new Location(loc.mECLoc * f);
		}

		/// <summary>
		/// Computation of derived values.
		/// This function re-computes the derived values like lat/lon and
		/// transformation matrices. It does this unconditionally.
		/// </summary>
		private void ComputeDerivedUnconditional()
		{
			// The radius is just the Euclidean norm of the vector.
			mRadius = mECLoc.GetMagnitude();

			// The distance of the location to the y-axis, which is the axis
			// through the poles.
			double rxy = Math.Sqrt(mECLoc.X*mECLoc.X + mECLoc.Y*mECLoc.Y);

			// Compute the sin/cos values of the longitude
			double sinLon, cosLon;
			if (rxy == 0.0) 
			{
				sinLon = 0.0;
				cosLon = 1.0;
			} 
			else 
			{
				sinLon = mECLoc.Y/rxy;
				cosLon = mECLoc.X/rxy;
			}

			// Compute the sin/cos values of the latitude
			double sinLat, cosLat;
			if (mRadius == 0.0)  
			{
				sinLat = 0.0;
				cosLat = 1.0;
			} 
			else 
			{
				sinLat = mECLoc.Z/mRadius;
				cosLat = rxy/mRadius;
			}

			// Compute the longitude and latitude itself
			if ( mECLoc.X == 0.0 && mECLoc.Y == 0.0 )
				mLon = 0.0;
			else
				mLon = Math.Atan2( mECLoc.Y, mECLoc.X );

			if ( rxy == 0.0 && mECLoc.Z == 0.0 )
				mLat = 0.0;
			else
				mLat = Math.Atan2( mECLoc.Z, rxy );

			// Compute the transform matrices from and to the earth centered frame.
			// see Durham Chapter 4, problem 1, page 52
			mTec2l = new Matrix3D( -cosLon*sinLat, -sinLon*sinLat,  cosLat,
				-sinLon   ,     cosLon    ,    0.0 ,
				-cosLon*cosLat, -sinLon*cosLat, -sinLat  );

			mTl2ec = mTec2l.GetTranspose();

			// Mark the cached values as valid
			mCacheValid = true;
		}
		
		/// <summary>
		/// Computation of derived values
		/// This function checks if the derived values like lat/lon and
		/// transformation matrices are already computed. If so, it
		/// returns. If they need to be computed this is done here.
		/// </summary>
		private void ComputeDerived() 
		{
			if (!mCacheValid)
				ComputeDerivedUnconditional();
		}


		/// <summary>
		/// Transform matrix from local horizontal to earth centered frame.
		/// Returns a copy of the rotation matrix of the transform from
		/// the local horizontal frame to the earth centered frame.
		/// </summary>
		public Matrix3D GetTl2ec() { ComputeDerived(); return mTl2ec; }

		/// <summary>
		/// Transform matrix from the earth centered to local horizontal frame.
		/// Returns a const reference to the rotation matrix of the transform from
		/// the earth centered frame to the local horizontal frame.
		/// </summary>
		public Matrix3D GetTec2l() { ComputeDerived(); return mTec2l; }

		/// <summary>
		/// Conversion from Local frame coordinates to a location in the
		/// earth centered and fixed frame.
		/// </summary>
		/// <param name="lvec">Vector in the local horizontal coordinate frame</param>
		/// <returns>The location in the earth centered and fixed frame</returns>
		public Location LocalToLocation(Vector3D lvec) 
		{
			ComputeDerived();
			return (Location)(mTl2ec*lvec + mECLoc);
		}

		/// <summary>
		/// Conversion from a location in the earth centered and fixed frame
		/// to local horizontal frame coordinates.
		/// </summary>
		/// <param name="ecvec">Vector in the earth centered and fixed frame</param>
		/// <returns>The vector in the local horizontal coordinate frame</returns>
		public Vector3D LocationToLocal(Vector3D ecvec) 
		{
			ComputeDerived();
			return mTec2l*(ecvec - mECLoc);
		}

		/// <summary>
		/// Converts the Vector3D to a location.
		/// </summary>
		/// <param name="v"></param>
		/// <returns></returns>
		public static explicit operator Location(Vector3D v) 
		{
			Location loc = new Location(v);
			return	loc;
		}

		public static explicit operator Vector3D(Location loc) 
		{
			return	loc.mECLoc;
		}

		/// <summary>
		/// The coordinates in the earth centered frame. This is the master copy.
		/// The coordinate frame has its center in the middle of the earth.
		/// Its x-axis points from the center of the earth towards a
		/// location with zero latitude and longitude on the earths
		/// surface. The y-axis points from the center of the earth towards a
		/// location with zero latitude and 90deg longitude on the earths
		/// surface. The z-axis points from the earths center to the
		/// geographic north pole.
		/// see W. C. Durham "Aircraft Dynamics & Control", section 2.2
		/// 
		/// </summary>
		private Vector3D mECLoc;

		/** The cached lon/lat/radius values. */
		private double mLon;
		private double mLat;
		private double mRadius;

		/** The cached rotation matrices from and to the associated frames. */
		private Matrix3D mTl2ec;
		private Matrix3D mTec2l;

		/// <summary>
		/// A data validity flag.
		/// This class implements caching of the derived values like the
		/// orthogonal rotation matrices or the lon/lat/radius values. For caching we
		/// carry a flag which signals if the values are valid or not.
		/// </summary>
		private bool mCacheValid = false;
	}
}
